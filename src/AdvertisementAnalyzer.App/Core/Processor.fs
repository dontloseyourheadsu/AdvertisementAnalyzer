namespace AdvertisementAnalyzer.Core

open OpenCvSharp
open Sdcb.PaddleOCR
open Sdcb.PaddleOCR.Models.Online
open Sdcb.PaddleInference
open System

type OcrResultItem = { Box: Point2f array; Text: string; Confidence: float32 }

type ImageProcessor(usePaddle: bool, lang: string) =
    let ocr =
        if usePaddle then
            try
                printfn "Initializing PaddleOCR (lang=%s)..." lang
                let model = 
                    if lang.ToLower().StartsWith("es") then
                        OnlineFullModels.ChineseV3.DownloadAsync() |> Async.AwaitTask |> Async.RunSynchronously
                    else
                        OnlineFullModels.EnglishV3.DownloadAsync() |> Async.AwaitTask |> Async.RunSynchronously
                let instance = new PaddleOcrAll(model, PaddleDevice.Mkldnn(), AllowRotateDetection = true)
                printfn "PaddleOCR loaded successfully."
                Some instance
            with ex ->
                printfn "Failed to initialize PaddleOCR: %A" ex
                None
        else None

    member this.DetectBillboardsCv(image: Mat) =
        let h = image.Rows
        let w = image.Cols
        let imgArea = float (h * w)

        use gray = new Mat()
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY)

        use blurred = new Mat()
        Cv2.BilateralFilter(gray, blurred, 9, 75.0, 75.0)

        use edges = new Mat()
        Cv2.Canny(blurred, edges, 50.0, 150.0)

        use kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15))
        use closed = new Mat()
        Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel)

        let outContours : Point[][] = [||]
        let outHierarchy = new Mat()
        
        // F# way to call out params for OpenCVSharp
        let mutable contours = [||]
        Cv2.FindContours(closed, &contours, outHierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple)

        let boxes =
            contours
            |> Array.choose (fun cnt ->
                let rect = Cv2.BoundingRect(cnt)
                let cntArea = float (rect.Width * rect.Height)
                let aspectRatio = float rect.Width / float rect.Height

                if 0.01 * imgArea < cntArea && cntArea < 0.85 * imgArea && 0.3 < aspectRatio && aspectRatio < 6.0 then
                    Some rect
                else None)
            |> Array.sortByDescending (fun r -> r.Width * r.Height)

        boxes |> Array.toList

    member this.RunOcr(imageCrop: Mat) =
        match ocr with
        | Some ocrEngine when not (imageCrop.Empty()) ->
            try
                let results = ocrEngine.Run(imageCrop)
                if not (isNull results) && not (isNull results.Regions) then
                    results.Regions
                    |> Array.map (fun r -> 
                        let points = r.Rect.Points()
                        { Box = points; Text = r.Text; Confidence = r.Score })
                    |> Array.toList
                else []
            with ex ->
                printfn "Exception during PaddleOCR execution: %s" ex.Message
                []
        | _ -> []

    member this.ClusterOcrResults(ocrResults: OcrResultItem list) =
        if List.isEmpty ocrResults then ""
        else
            let highConfResults = ocrResults |> List.filter (fun r -> r.Confidence >= 0.1f)
            if List.isEmpty highConfResults then ""
            elif highConfResults.Length = 1 then highConfResults.[0].Text
            else
                let centersAndHeights =
                    highConfResults |> List.map (fun r ->
                        let xs = r.Box |> Array.map (fun p -> float p.X)
                        let ys = r.Box |> Array.map (fun p -> float p.Y)
                        let yMin, yMax = Array.min ys, Array.max ys
                        let cx, cy = Array.sum xs / 4.0, Array.sum ys / 4.0
                        (cx, cy), (yMax - yMin)
                    )
                let avgHeight =
                    centersAndHeights |> List.averageBy snd

                let eps = max (1.5 * avgHeight) 15.0

                let features = centersAndHeights |> List.map (fun ((cx, cy), _) -> (cx / 10.0, cy)) |> List.toArray

                let n = features.Length
                let visited = Array.create n false
                let cluster = Array.create n -1

                let dist (x1: float, y1: float) (x2: float, y2: float) =
                    Math.Sqrt((x1 - x2) ** 2.0 + (y1 - y2) ** 2.0)

                let regionQuery i =
                    [| for j in 0 .. n - 1 do
                        if dist features.[i] features.[j] <= eps then yield j |]

                let mutable c = -1
                for i in 0 .. n - 1 do
                    if not visited.[i] then
                        visited.[i] <- true
                        let neighbors = regionQuery i
                        if neighbors.Length >= 1 then
                            c <- c + 1
                            cluster.[i] <- c
                            let mutable q = System.Collections.Generic.Queue(neighbors)
                            while q.Count > 0 do
                                let p = q.Dequeue()
                                if not visited.[p] then
                                    visited.[p] <- true
                                    let pNeighbors = regionQuery p
                                    if pNeighbors.Length >= 1 then
                                        for pn in pNeighbors do q.Enqueue(pn)
                                if cluster.[p] = -1 then
                                    cluster.[p] <- c

                let grouped =
                    highConfResults
                    |> List.mapi (fun i r -> cluster.[i], r, features.[i])
                    |> List.groupBy (fun (c, _, _) -> c)
                    |> List.map (fun (_, items) ->
                        let avgY = items |> List.averageBy (fun (_, _, (_, cy)) -> cy)
                        let sortedItems = items |> List.sortBy (fun (_, _, (cx, _)) -> cx)
                        avgY, sortedItems
                    )
                    |> List.sortBy fst

                let lines =
                    grouped
                    |> List.map (fun (_, items) ->
                        items |> List.map (fun (_, r, _) -> r.Text) |> String.concat " "
                    )

                String.concat "\n" lines
