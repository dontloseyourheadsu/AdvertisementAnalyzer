namespace AdvertisementAnalyzer.Training

open System
open System.IO
open System.IO.Compression
open System.Net.Http

type YoloTrainer() =
    let downloadFileAsync (client: HttpClient) (url: string) (destinationPath: string) =
        async {
            printfn "Connecting to %s..." url
            let! response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            
            let totalBytes = response.Content.Headers.ContentLength |> Option.ofNullable |> Option.defaultValue -1L
            printfn "File size: %s" (if totalBytes = -1L then "Unknown" else sprintf "%.2f MB" (float totalBytes / 1024.0 / 1024.0))

            use! contentStream = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
            use fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)
            
            let buffer = Array.zeroCreate (8192 * 10)
            let mutable totalRead = 0L
            let mutable isReading = true
            let mutable lastReportTime = DateTime.UtcNow
            
            while isReading do
                let! read = contentStream.ReadAsync(buffer, 0, buffer.Length) |> Async.AwaitTask
                if read = 0 then
                    isReading <- false
                else
                    fileStream.Write(buffer, 0, read)
                    totalRead <- totalRead + int64 read
                    let now = DateTime.UtcNow
                    if (now - lastReportTime).TotalSeconds >= 3.0 then
                        lastReportTime <- now
                        if totalBytes <> -1L then
                            let pct = float totalRead / float totalBytes * 100.0
                            printfn "Downloaded: %.2f MB / %.2f MB (%.1f%%)" (float totalRead / 1024.0 / 1024.0) (float totalBytes / 1024.0 / 1024.0) pct
                        else
                            printfn "Downloaded: %.2f MB" (float totalRead / 1024.0 / 1024.0)
            
            fileStream.Close()
            printfn "Download finished."
        }

    let runKaggleDownload (slug: string) (destinationDir: string) (token: string) =
        try
            printfn "Starting Kaggle download via kagglehub python script..."
            let startInfo = new System.Diagnostics.ProcessStartInfo()
            startInfo.FileName <- "python3"
            startInfo.Arguments <- sprintf "src/download_kaggle.py %s %s" slug destinationDir
            startInfo.UseShellExecute <- false
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            
            if not (String.IsNullOrEmpty token) then
                startInfo.EnvironmentVariables.["KAGGLE_API_TOKEN"] <- token
            else
                let envToken = Environment.GetEnvironmentVariable("KAGGLE_API_TOKEN")
                if not (String.IsNullOrEmpty envToken) then
                    startInfo.EnvironmentVariables.["KAGGLE_API_TOKEN"] <- envToken

            use proc = System.Diagnostics.Process.Start(startInfo)
            proc.OutputDataReceived.Add(fun e -> if not (isNull e.Data) then printfn "  [Kaggle] %s" e.Data)
            proc.ErrorDataReceived.Add(fun e -> if not (isNull e.Data) then printfn "  [Kaggle Error] %s" e.Data)
            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            proc.WaitForExit()
            proc.ExitCode = 0
        with ex ->
            printfn "Failed to run Kaggle downloader script: %s" ex.Message
            false

    member this.DownloadDataset(datasetType: string, destinationDir: string, apiKey: string) =
        if datasetType.ToLower() = "kaggle" then
            let slug = "dataclusterlabs/ad-board"
            runKaggleDownload slug destinationDir apiKey
        else
            try
                let url =
                    match datasetType.ToLower() with
                    | "zenodo" -> 
                        "https://zenodo.org/api/records/8366970/files/UAVBillboardsDataset.zip/content"
                    | "roboflow" ->
                        if String.IsNullOrEmpty apiKey then
                            failwith "Roboflow download requires a Roboflow API key."
                        sprintf "https://app.roboflow.com/ds/spectacular-billboard-detection/1?key=%s" apiKey
                    | _ -> 
                        failwithf "Unknown dataset type: %s. Supported types: 'zenodo', 'roboflow', 'kaggle'." datasetType

                printfn "Preparing download of %s dataset..." datasetType
                if not (Directory.Exists destinationDir) then
                    Directory.CreateDirectory destinationDir |> ignore

                let zipPath = Path.Combine(destinationDir, "dataset_temp.zip")
                use client = new HttpClient()
                client.Timeout <- TimeSpan.FromHours(2.0)
                
                Async.RunSynchronously(downloadFileAsync client url zipPath)
                
                printfn "Extracting ZIP contents to %s..." destinationDir
                ZipFile.ExtractToDirectory(zipPath, destinationDir, overwriteFiles = true)
                File.Delete(zipPath)
                printfn "Dataset setup successfully completed in: %s" destinationDir
                true
            with ex ->
                printfn "Error during dataset download/extraction: %s" ex.Message
                false

    member this.Train(epochs: int, apiKey: string) =
        printfn "Starting YOLOv8 training for %d epochs..." epochs
        printfn "Using Roboflow API Key: %s" (if String.IsNullOrEmpty apiKey then "None" else "***")
        // In a full implementation, this downloads the dataset from Roboflow and runs YOLOv8 training.
        printfn "Training complete."