namespace AdvertisementAnalyzer

open System

type BoundingBox = {
    X: float
    Y: float
    Width: float
    Height: float
}

type StructureType =
    | Underpass
    | Highway
    | TollBooth
    | Column
    | Stadium
    | Kiosk
    | Panel

type DetectedObject = {
    Label: string
    Confidence: float
    Box: BoundingBox option
}

type AdvertisementAnalysisResult = {
    ImagePath: string
    OcrText: string
    DetectedObjects: DetectedObject list
    AdvertisementZone: BoundingBox option
    StructureType: StructureType option
    ContentKeywords: string list
    Caption: string
    IsLikelyAdvertisement: bool
    CompletedAtUtc: DateTime
}

type CaptionProviderKind =
    | FlorencePlaceholder
    | MoondreamSidecar
    | Claude

module CaptionProviderKind =
    let parse (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "florence" -> FlorencePlaceholder
        | "moondream-sidecar" -> MoondreamSidecar
        | "claude" -> Claude
        | _ -> FlorencePlaceholder


type PipelineConfig = {
    ImagePath: string
    YoloModelPath: string
    CaptionProvider: CaptionProviderKind
    CaptionEndpoint: string option
    CaptionApiKey: string option
    CaptionModel: string option
}
