namespace AdvertisementAnalyzer

open Sdcb.PaddleOCR
open Sdcb.PaddleOCR.Models.Online
open OpenCvSharp

type PaddleOcrProvider() =
    interface IOcrProvider with
        member _.ExtractTextAsync(imagePath: string) = async {
            let! model = OnlineFullModels.EnglishV3.DownloadAsync() |> Async.AwaitTask
            use ocr =
                new PaddleOcrAll(
                    model,
                    PaddleDevice.Mkldnn(),
                    AllowRotateDetection = true)

            use src = Cv2.ImRead(imagePath)
            let result = ocr.Run(src)
            return result.Text
        }
