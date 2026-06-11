namespace AdvertisementAnalyzer.Core

open System
open System.IO
open System.Text.Json
open OpenCvSharp
open AdvertisementAnalyzer.Refiners

type StructureData = {
    ImageName: string
    StructureIndex: int
    DetectionMethod: string
    Box: int[]
    StandardOcr: string
    VisualOcr: string
    Caption: string
    Details: AdDetails
}

type AnalysisPipeline(useDetect: bool, useVlm: bool, lang: string, yoloWeights: string, modelPath: string option) =
    let processor = new ImageProcessor(usePaddle = true, lang = lang)
    
    let yoloDetector =
        if useDetect then
            try
                printfn "Loading YOLO weights from: %s..." yoloWeights
                Some(new YoloDetector(yoloWeights))
            with ex ->
                printfn "Failed to load YOLO model: %A" ex
                printfn "Will fall back to CV contour-based detection."
                None
        else None

    let florence = if useVlm then Some(new FlorenceRefiner()) else None
    let llamaJudge = if useVlm then Some(new LlamaJudge(modelPath)) else None

    member this.RunImage(imagePath: string, outputDir: string) : StructureData list =
        let fileInfo = new FileInfo(imagePath)
        printfn "\nProcessing image: %s" fileInfo.Name

        use image = Cv2.ImRead(imagePath)
        if image.Empty() then
            printfn "Error: Could not read image %s" imagePath
            []
        else
            let h = image.Rows
            let w = image.Cols

            let mutable detections = []
            
            if useDetect then
                match yoloDetector with
                | Some yolo ->
                    try
                        let results = yolo.Detect(imagePath)
                        for r in results do
                            match r.Box with
                            | Some box ->
                                let x1 = int box.X
                                let y1 = int box.Y
                                let x2 = int (box.X + box.Width)
                                let y2 = int (box.Y + box.Height)
                                detections <- (x1, y1, x2, y2, "yolo") :: detections
                            | None -> ()
                    with ex ->
                        printfn "YOLO inference error: %s" ex.Message
                | None -> ()

                if List.isEmpty detections then
                    printfn "No billboards detected by YOLO. Running CV contour fallback..."
                    let cvBoxes = processor.DetectBillboardsCv(image)
                    for r in cvBoxes do
                        detections <- (r.X, r.Y, r.X + r.Width, r.Y + r.Height, "cv_fallback") :: detections

                if List.isEmpty detections then
                    printfn "No billboards detected by CV fallback. Analyzing full image."
                    detections <- (0, 0, w, h, "full_image") :: detections
            else
                detections <- (0, 0, w, h, "full_image") :: detections

            detections <- List.rev detections
            printfn "Found %d candidate structures for analysis." detections.Length

            use annotatedImage = image.Clone()
            let mutable structures = []

            detections |> List.iteri (fun idx (x1, y1, x2, y2, method) ->
                let cx1 = max 0 x1
                let cy1 = max 0 y1
                let cx2 = min w x2
                let cy2 = min h y2

                if (cx2 - cx1) > 10 && (cy2 - cy1) > 10 then
                    printfn "Structure %d/%d (%s): Box [%d, %d, %d, %d]" (idx + 1) detections.Length method cx1 cy1 cx2 cy2
                    
                    let rect = new OpenCvSharp.Rect(cx1, cy1, cx2 - cx1, cy2 - cy1)
                    use crop = new Mat(image, rect)

                    let rawOcr = processor.RunOcr(crop)
                    let standardText = processor.ClusterOcrResults(rawOcr)
                    printfn "  Standard OCR Output:\n  ---\n  %s\n  ---" (standardText.Replace("\n", " | "))

                    let mutable visualOcr = ""
                    let mutable caption = ""
                    let mutable details = { Brand = "Generico"; Category = "Otros"; StructureType = "Espectacular"; TextContent = standardText; Items = [||] }

                    if useVlm then
                        match florence with
                        | Some f ->
                            let vlmResults = f.RefineCrop(crop)
                            visualOcr <- vlmResults.["visual_ocr"]
                            caption <- vlmResults.["image_caption"]
                            printfn "  Visual OCR (Florence): %s" visualOcr
                            printfn "  Visual Caption (Florence): %s" caption
                        | None -> ()

                        match llamaJudge with
                        | Some j ->
                            details <- j.Reconcile(standardText, visualOcr, caption, lang)
                            printfn "  Reconciled Details (Judge):\n    Brand: %s\n    Category: %s\n    Structure Type: %s\n    Text: %s\n    Items: %s" 
                                details.Brand details.Category details.StructureType details.TextContent (String.concat ", " details.Items)
                        | None ->
                            let tempJudge = new LlamaJudge(None)
                            details <- tempJudge.Reconcile(standardText, visualOcr, caption, lang)
                    else
                        let tempJudge = new LlamaJudge(None)
                        details <- tempJudge.Reconcile(standardText, visualOcr, caption, lang)

                    structures <- {
                        ImageName = fileInfo.Name
                        StructureIndex = idx
                        DetectionMethod = method
                        Box = [| cx1; cy1; cx2; cy2 |]
                        StandardOcr = standardText
                        VisualOcr = visualOcr
                        Caption = caption
                        Details = details
                    } :: structures

                    let boxColor = 
                        match method with
                        | "yolo" -> new Scalar(0.0, 120.0, 255.0) // BGR for Azure (255, 120, 0 in RGB)
                        | "cv_fallback" -> new Scalar(255.0, 165.0, 0.0) // BGR for Orange
                        | _ -> new Scalar(0.0, 200.0, 0.0) // Green

                    Cv2.Rectangle(annotatedImage, new OpenCvSharp.Point(cx1, cy1), new OpenCvSharp.Point(cx2, cy2), boxColor, 3)

                    let labelTextForImage = 
                        if not (String.IsNullOrEmpty details.Brand) && details.Brand <> "Generico" then
                            sprintf "#%d: %s | %s" (idx + 1) details.Brand details.StructureType
                        else
                            sprintf "#%d: %s" (idx + 1) details.StructureType
                    let label = if labelTextForImage.Length > 30 then sprintf "%s..." (labelTextForImage.Substring(0, 30)) else labelTextForImage
                    let labelText = if String.IsNullOrWhiteSpace(label) then sprintf "#%d: [No Brand]" (idx + 1) else label

                    let mutable baseline = 0
                    let textSize = Cv2.GetTextSize(labelText, HersheyFonts.HersheySimplex, 0.6, 2, &baseline)
                    let labelY1 = max 0 (cy1 - textSize.Height - 10)
                    let labelY2 = cy1

                    Cv2.Rectangle(annotatedImage, new OpenCvSharp.Point(cx1, labelY1), new OpenCvSharp.Point(cx1 + textSize.Width + 10, labelY2), boxColor, -1)
                    Cv2.PutText(annotatedImage, labelText, new OpenCvSharp.Point(cx1 + 5, cy1 - 5), HersheyFonts.HersheySimplex, 0.6, new Scalar(255.0, 255.0, 255.0), 2, LineTypes.AntiAlias)
            )

            let outPath = Path.Combine(outputDir, "annotated_" + fileInfo.Name)
            Cv2.ImWrite(outPath, annotatedImage) |> ignore
            printfn "Annotated image saved to %s" outPath

            List.rev structures

    member this.RunPipeline(datasetDir: string, outputDir: string) =
        if not (Directory.Exists(datasetDir)) then
            failwithf "Dataset directory '%s' does not exist." datasetDir

        Directory.CreateDirectory(outputDir) |> ignore

        let validExtensions = set [| ".png"; ".jpg"; ".jpeg"; ".bmp"; ".tiff"; ".webp" |]
        let imageFiles = 
            Directory.GetFiles(datasetDir)
            |> Array.filter (fun f -> validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))

        printfn "Found %d images in dataset directory '%s'." imageFiles.Length datasetDir
        
        if imageFiles.Length > 0 then
            let start = DateTime.UtcNow
            let mutable allStructures = []

            for imgPath in imageFiles do
                try
                    let results = this.RunImage(imgPath, outputDir)
                    allStructures <- allStructures @ results
                with ex ->
                    printfn "Failed to process image %s: %s" (Path.GetFileName(imgPath)) ex.Message

            let csvPath = Path.Combine(outputDir, "analysis_report.csv")
            printfn "\nWriting summary report to: %s" csvPath

            try
                use writer = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8)
                writer.WriteLine("image_name,structure_index,detection_method,box_coords,standard_ocr,visual_ocr,caption,brand,category,structure_type,text_content,items")
                for s in allStructures do
                    let boxJson = JsonSerializer.Serialize(s.Box)
                    let itemsJson = JsonSerializer.Serialize(s.Details.Items)
                    let escapeCsv (text: string) =
                        if String.IsNullOrEmpty(text) then "\"\""
                        else "\"" + text.Replace("\"", "\"\"") + "\""
                    
                    let line = sprintf "%s,%d,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s" 
                                    (escapeCsv s.ImageName) s.StructureIndex s.DetectionMethod (escapeCsv boxJson) 
                                    (escapeCsv s.StandardOcr) (escapeCsv s.VisualOcr) (escapeCsv s.Caption)
                                    (escapeCsv s.Details.Brand) (escapeCsv s.Details.Category) (escapeCsv s.Details.StructureType)
                                    (escapeCsv s.Details.TextContent) (escapeCsv itemsJson)
                    writer.WriteLine(line)
                printfn "CSV report generated successfully."
            with ex ->
                printfn "Failed to write CSV report: %s" ex.Message

            let elapsed = (DateTime.UtcNow - start).TotalSeconds
            printfn "\n=== Pipeline Completed in %.2f seconds ===" elapsed
            printfn "Analyzed %d images, found %d structures." imageFiles.Length allStructures.Length
            printfn "Outputs saved under: %s" (Path.GetFullPath(outputDir))
