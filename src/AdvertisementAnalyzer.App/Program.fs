namespace AdvertisementAnalyzer

open System
open System.IO
open System.Text.Json

module Program =
    let private tryGetArg (name: string) (args: string array) =
        args
        |> Array.tryFindIndex ((=) name)
        |> Option.bind (fun i -> if i + 1 < args.Length then Some args[i + 1] else None)

    let private hasFlag (name: string) (args: string array) =
        args |> Array.exists ((=) name)

    let private requireArg name args =
        tryGetArg name args
        |> Option.defaultWith (fun _ -> failwith $"Missing required argument: {name}")

    let private createCaptionProvider (config: PipelineConfig) =
        match config.CaptionProvider with
        | FlorencePlaceholder ->
            new FlorencePlaceholderCaptionProvider() :> ICaptionProvider
        | MoondreamSidecar ->
            let endpoint = config.CaptionEndpoint |> Option.defaultValue "http://localhost:8000/describe"
            new MoondreamSidecarCaptionProvider(endpoint) :> ICaptionProvider
        | Claude ->
            let key =
                config.CaptionApiKey
                |> Option.orElseWith (fun _ -> Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") |> Option.ofObj)
                |> Option.defaultWith (fun _ -> failwith "Missing --caption-api-key or ANTHROPIC_API_KEY")
            let model = config.CaptionModel |> Option.defaultValue "claude-3-5-sonnet-latest"
            new ClaudeCaptionProvider(key, model) :> ICaptionProvider

    let private printUsage () =
        printfn "Usage:"
        printfn "  dotnet run --project src/AdvertisementAnalyzer.App -- --image <path> --yolo-model <path> [--caption-provider florence|moondream-sidecar|claude] [--caption-endpoint <url>] [--caption-api-key <key>] [--caption-model <name>]"

    [<EntryPoint>]
    let main argv =
        try
            if hasFlag "--help" argv || argv.Length = 0 then
                printUsage ()
                0
            else
                let imagePath = requireArg "--image" argv
                let yoloModelPath = requireArg "--yolo-model" argv

                if not (File.Exists imagePath) then
                    failwith $"Image not found: {imagePath}"

                let captionProvider =
                    tryGetArg "--caption-provider" argv
                    |> Option.defaultValue "florence"
                    |> CaptionProviderKind.parse

                let config = {
                    ImagePath = imagePath
                    YoloModelPath = yoloModelPath
                    CaptionProvider = captionProvider
                    CaptionEndpoint = tryGetArg "--caption-endpoint" argv
                    CaptionApiKey = tryGetArg "--caption-api-key" argv
                    CaptionModel = tryGetArg "--caption-model" argv
                }

                let ocr = new PaddleOcrProvider() :> IOcrProvider
                let detector = new YoloObjectDetector(config.YoloModelPath) :> IObjectDetector
                let captionProviderInstance = createCaptionProvider config

                use disposableCaption =
                    match captionProviderInstance with
                    | :? IDisposable as d -> d
                    | _ -> null

                let pipeline = new AdvertisementPipeline(ocr, detector, captionProviderInstance)
                let result = pipeline.RunAsync(config) |> Async.RunSynchronously

                let json = JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true))
                printfn "%s" json
                0
        with ex ->
            eprintfn "Error: %s" ex.Message
            1
