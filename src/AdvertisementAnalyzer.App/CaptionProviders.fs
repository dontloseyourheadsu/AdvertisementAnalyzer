namespace AdvertisementAnalyzer

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json

type FlorencePlaceholderCaptionProvider() =
    interface ICaptionProvider with
        member _.DescribeAsync(imagePath, ocrText, objects) = async {
            let objectLabels = objects |> List.map (fun o -> o.Label) |> String.concat ", "
            return
                $"[Florence-2 placeholder] Implement ONNX Runtime VLM inference here. OCR='{ocrText}'. Objects='{objectLabels}'. Image='{imagePath}'."
        }

    interface IDisposable with
        member _.Dispose() = ()

type MoondreamSidecarCaptionProvider(endpoint: string) =
    let http = new HttpClient()

    interface ICaptionProvider with
        member _.DescribeAsync(imagePath, ocrText, objects) = async {
            let labels = objects |> List.map (fun o -> o.Label)
            let payload =
                JsonSerializer.Serialize({|
                    imagePath = imagePath
                    ocrText = ocrText
                    objects = labels
                |})

            use content = new StringContent(payload, Encoding.UTF8, "application/json")
            let! response = http.PostAsync(endpoint, content) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

            try
                use doc = JsonDocument.Parse(body)
                let mutable caption = Unchecked.defaultof<JsonElement>
                if doc.RootElement.TryGetProperty("caption", &caption) then
                    return caption.GetString()
                else
                    return body
            with _ ->
                return body
        }

    interface IDisposable with
        member _.Dispose() = http.Dispose()

type ClaudeCaptionProvider(apiKey: string, model: string) =
    let http = new HttpClient(BaseAddress = Uri("https://api.anthropic.com"))

    do
        http.DefaultRequestHeaders.Add("x-api-key", apiKey)
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01")
        http.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))

    interface ICaptionProvider with
        member _.DescribeAsync(imagePath, ocrText, objects) = async {
            let objectLabels = objects |> List.map (fun o -> o.Label) |> String.concat ", "
            let prompt =
                $"Analyze this advertisement image context. Image path: {imagePath}. OCR text: {ocrText}. Detected objects: {objectLabels}. Summarize likely ad content and intent in 3-4 sentences."

            let payload =
                JsonSerializer.Serialize({|
                    model = model
                    max_tokens = 350
                    messages = [|
                        {| role = "user"; content = prompt |}
                    |]
                |})

            use content = new StringContent(payload, Encoding.UTF8, "application/json")
            let! response = http.PostAsync("/v1/messages", content) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

            use doc = JsonDocument.Parse(body)
            let contentNode = doc.RootElement.GetProperty("content")
            if contentNode.GetArrayLength() = 0 then return ""
            else return contentNode[0].GetProperty("text").GetString()
        }

    interface IDisposable with
        member _.Dispose() = http.Dispose()
