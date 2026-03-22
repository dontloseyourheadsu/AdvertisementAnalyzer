namespace AdvertisementAnalyzer

open System

module AdvertisementHeuristics =
    let private adLabels =
        [ "billboard"; "poster"; "banner"; "sign"; "product"; "logo"; "display" ]

    let private structureHints: (StructureType * string list) list =
        [
            Underpass, [ "underpass"; "bridge"; "viaduct" ]
            Highway, [ "highway"; "road"; "route"; "freeway" ]
            TollBooth, [ "booth"; "toll"; "checkpoint"; "gate" ]
            Column, [ "column"; "pillar"; "pole" ]
            Stadium, [ "stadium"; "arena"; "bleachers" ]
            Kiosk, [ "kiosk"; "newsstand"; "stall" ]
            Panel, [ "panel"; "board"; "hoarding" ]
        ]

    let private tokenSplitChars = [| ' '; '\n'; '\r'; '\t'; ','; '.'; ';'; ':'; '!'; '?'; '"'; '\''; '('; ')'; '['; ']'; '{'; '}'; '/' |]

    let private tokenize (text: string) =
        text.Split(tokenSplitChars, StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.toList

    let isLikelyAdvertisement (objects: DetectedObject list) (ocrText: string) =
        let hasAdLikeObject =
            objects
            |> List.exists (fun o ->
                adLabels
                |> List.exists (fun label ->
                    o.Label.Contains(label, StringComparison.OrdinalIgnoreCase)))

        let hasOcrText = not (String.IsNullOrWhiteSpace ocrText)
        hasAdLikeObject || hasOcrText

    let tryGetAdvertisementZone (objects: DetectedObject list) =
        let adBoxes =
            objects
            |> List.filter (fun o ->
                adLabels
                |> List.exists (fun label -> o.Label.Contains(label, StringComparison.OrdinalIgnoreCase)))
            |> List.choose (fun o -> o.Box)

        let candidateBoxes =
            if List.isEmpty adBoxes then
                objects |> List.choose (fun o -> o.Box)
            else
                adBoxes

        match candidateBoxes with
        | [] -> None
        | first :: rest ->
            let left =
                candidateBoxes
                |> List.map (fun b -> b.X)
                |> List.min

            let top =
                candidateBoxes
                |> List.map (fun b -> b.Y)
                |> List.min

            let right =
                candidateBoxes
                |> List.map (fun b -> b.X + b.Width)
                |> List.max

            let bottom =
                candidateBoxes
                |> List.map (fun b -> b.Y + b.Height)
                |> List.max

            Some {
                X = left
                Y = top
                Width = max first.Width (right - left)
                Height = max first.Height (bottom - top)
            }

    let classifyStructureType (objects: DetectedObject list) (ocrText: string) =
        let joinedLabels =
            objects
            |> List.map (fun o -> o.Label)
            |> String.concat " "

        let evidence = $"{ocrText} {joinedLabels}"

        structureHints
        |> List.tryPick (fun (structureType, hints) ->
            if hints |> List.exists (fun hint -> evidence.Contains(hint, StringComparison.OrdinalIgnoreCase)) then
                Some structureType
            else
                None)

    let extractContentKeywords (ocrText: string) =
        if String.IsNullOrWhiteSpace ocrText then
            []
        else
            tokenize ocrText
            |> List.filter (fun token -> token.Length >= 4)
            |> List.distinct
            |> List.truncate 12

type AdvertisementPipeline(ocr: IOcrProvider, detector: IObjectDetector, caption: ICaptionProvider) =
    member _.RunAsync(config: PipelineConfig) = async {
        let! ocrText = ocr.ExtractTextAsync(config.ImagePath)
        let! objects = detector.DetectAsync(config.ImagePath)
        let! description = caption.DescribeAsync(config.ImagePath, ocrText, objects)

        let adZone = AdvertisementHeuristics.tryGetAdvertisementZone objects
        let structureType = AdvertisementHeuristics.classifyStructureType objects ocrText
        let keywords = AdvertisementHeuristics.extractContentKeywords ocrText

        return {
            ImagePath = config.ImagePath
            OcrText = ocrText
            DetectedObjects = objects
            AdvertisementZone = adZone
            StructureType = structureType
            ContentKeywords = keywords
            Caption = description
            IsLikelyAdvertisement = AdvertisementHeuristics.isLikelyAdvertisement objects ocrText
            CompletedAtUtc = DateTime.UtcNow
        }
    }
