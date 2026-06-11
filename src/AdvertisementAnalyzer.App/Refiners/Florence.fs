namespace AdvertisementAnalyzer.Refiners

open System
open System.IO
open System.Net.Http
open OpenCvSharp

type FlorenceRefiner() =
    let client = new HttpClient()
    let serviceUrl = 
        let envUrl = Environment.GetEnvironmentVariable("VLM_SERVICE_URL")
        if String.IsNullOrEmpty(envUrl) then "http://localhost:5000/refine" else envUrl

    // This represents the Florence-2 model for visual OCR and captioning.
    // It processes the image crop and extracts information.
    member this.RefineCrop(crop: Mat) =
        try
            // Encode image as PNG
            let success, bytes = Cv2.ImEncode(".png", crop)
            if not success then
                printfn "Warning: Failed to encode image crop to PNG."
                Map.ofList [ "visual_ocr", ""; "image_caption", "Error encoding image" ]
            else
                use content = new ByteArrayContent(bytes)
                content.Headers.ContentType <- System.Net.Http.Headers.MediaTypeHeaderValue.Parse("image/png")
                
                let response = client.PostAsync(serviceUrl, content) |> Async.AwaitTask |> Async.RunSynchronously
                if response.IsSuccessStatusCode then
                    let json = response.Content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
                    
                    // Deserialize the JSON response
                    use doc = System.Text.Json.JsonDocument.Parse(json)
                    let root = doc.RootElement
                    let visualOcr = if root.TryGetProperty("visual_ocr") |> fst then root.GetProperty("visual_ocr").GetString() else ""
                    let caption = if root.TryGetProperty("image_caption") |> fst then root.GetProperty("image_caption").GetString() else ""
                    
                    Map.ofList [
                        "visual_ocr", visualOcr
                        "image_caption", caption
                    ]
                else
                    printfn "Warning: VLM Service returned HTTP %A" response.StatusCode
                    Map.ofList [ "visual_ocr", ""; "image_caption", "VLM service error" ]
        with ex ->
            printfn "Warning: VLM Service call failed: %s. Using default mock values." ex.Message
            Map.ofList [ "visual_ocr", ""; "image_caption", "VLM service unreachable" ]