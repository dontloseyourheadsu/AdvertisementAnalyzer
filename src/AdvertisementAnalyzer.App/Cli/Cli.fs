namespace AdvertisementAnalyzer.Cli

open System
open AdvertisementAnalyzer.Core
open AdvertisementAnalyzer.Training

type CliMenu() =
    member this.RunInteractive() =
        printfn "=== Street Advertisement Analyzer ==="
        printfn "1. Run Analysis Pipeline"
        printfn "2. Train YOLO Model"
        printfn "3. Exit"
        printf "Select an option: "
        
        match Console.ReadLine() with
        | "1" ->
            let pipeline = new AnalysisPipeline(useDetect = true, useVlm = true, lang = "es", yoloWeights = "billboard_best.pt", modelPath = None)
            pipeline.RunPipeline("dataset", "output")
        | "2" ->
            let trainer = new YoloTrainer()
            trainer.Train(20, "")
        | _ -> ()

    member this.ParseArgs(args: string array) =
        if args.Length = 0 then
            this.RunInteractive()
        else
            let command = args.[0]
            if command = "run" then
                let useDetect = args |> Array.contains "--detect"
                let useVlm = args |> Array.contains "--vlm"
                
                let lang = 
                    let idx = args |> Array.tryFindIndex ((=) "--lang")
                    match idx with
                    | Some i when i + 1 < args.Length -> args.[i + 1]
                    | _ -> "es"

                let dataset = 
                    let idx = args |> Array.tryFindIndex ((=) "--dataset")
                    match idx with
                    | Some i when i + 1 < args.Length -> args.[i + 1]
                    | _ -> "dataset"

                let output = 
                    let idx = args |> Array.tryFindIndex ((=) "--output")
                    match idx with
                    | Some i when i + 1 < args.Length -> args.[i + 1]
                    | _ -> "output"

                let yoloWeights = 
                    let idx = args |> Array.tryFindIndex ((=) "--yolo-weights")
                    match idx with
                    | Some i when i + 1 < args.Length -> args.[i + 1]
                    | _ -> "billboard_best.pt"

                let pipeline = new AnalysisPipeline(useDetect, useVlm, lang, yoloWeights, None)
                pipeline.RunPipeline(dataset, output)
                
            elif command = "train" then
                let epochs = 
                    let idx = args |> Array.tryFindIndex ((=) "--epochs")
                    match idx with
                    | Some i when i + 1 < args.Length -> 
                        match Int32.TryParse(args.[i + 1]) with
                        | true, v -> v
                        | _ -> 20
                    | _ -> 20
                    
                let apiKey = 
                    let idx = args |> Array.tryFindIndex ((=) "--api-key")
                    match idx with
                    | Some i when i + 1 < args.Length -> args.[i + 1]
                    | _ -> ""
                    
                let trainer = new YoloTrainer()
                trainer.Train(epochs, apiKey)
            else
                printfn "Unknown command: %s" command
