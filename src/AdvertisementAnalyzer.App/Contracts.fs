namespace AdvertisementAnalyzer

type IOcrProvider =
    abstract member ExtractTextAsync: imagePath: string -> Async<string>

type IObjectDetector =
    abstract member DetectAsync: imagePath: string -> Async<DetectedObject list>

type ICaptionProvider =
    abstract member DescribeAsync: imagePath: string * ocrText: string * objects: DetectedObject list -> Async<string>
