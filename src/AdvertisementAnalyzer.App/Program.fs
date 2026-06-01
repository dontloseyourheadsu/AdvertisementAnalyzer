namespace AdvertisementAnalyzer

open System
open AdvertisementAnalyzer.Cli

module Program =
    [<EntryPoint>]
    let main args =
        try
            let cli = new CliMenu()
            cli.ParseArgs(args)
            0
        with ex ->
            printfn "Error: %s" ex.Message
            1
