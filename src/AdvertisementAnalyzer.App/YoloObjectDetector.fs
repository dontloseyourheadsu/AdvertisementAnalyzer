namespace AdvertisementAnalyzer

open System
open System.IO
open System.Reflection

// This adapter uses reflection so it can tolerate minor YoloDotNet API evolution.
type YoloObjectDetector(modelPath: string) =
    let loadYolo() =
        let asm =
            AppDomain.CurrentDomain.GetAssemblies()
            |> Array.tryFind (fun a -> a.GetName().Name = "YoloDotNet")
            |> Option.defaultWith (fun _ -> Assembly.Load("YoloDotNet"))

        let yoloType =
            asm.GetType("YoloDotNet.Yolo")
            |> Option.ofObj
            |> Option.defaultWith (fun _ -> failwith "YoloDotNet.Yolo type was not found.")

        let optionsType =
            asm.GetType("YoloDotNet.Models.YoloOptions")
            |> Option.ofObj
            |> Option.defaultWith (fun _ -> failwith "YoloDotNet.Models.YoloOptions type was not found.")

        let options = Activator.CreateInstance(optionsType)
        let onnxProp = optionsType.GetProperty("OnnxModel")
        onnxProp.SetValue(options, modelPath)

        let modelTypeProp = optionsType.GetProperty("ModelType")
        if not (isNull modelTypeProp) then
            let enumType = modelTypeProp.PropertyType
            let enumName =
                Enum.GetNames(enumType)
                |> Array.tryFind (fun n -> n.Contains("ObjectDetection", StringComparison.OrdinalIgnoreCase))
                |> Option.defaultValue (Enum.GetNames(enumType) |> Array.head)
            let enumValue = Enum.Parse(enumType, enumName)
            modelTypeProp.SetValue(options, enumValue)

        let ctor =
            yoloType.GetConstructors()
            |> Array.tryFind (fun c ->
                let p = c.GetParameters()
                p.Length = 1 && p[0].ParameterType = optionsType)
            |> Option.defaultWith (fun _ ->
                failwith "Compatible Yolo constructor not found.")

        ctor.Invoke([| options |]), yoloType

    let tryGetFloat (name: string) (instance: obj) =
        let prop = instance.GetType().GetProperty(name)
        if isNull prop then None
        else
            let value = prop.GetValue(instance)
            if isNull value then None else Some(Convert.ToDouble(value))

    let tryGetString (names: string list) (instance: obj) =
        names
        |> List.tryPick (fun name ->
            let prop = instance.GetType().GetProperty(name)
            if isNull prop then None
            else
                let value = prop.GetValue(instance)
                if isNull value then None else Some(string value))

    let tryGetBoundingBox (instance: obj) =
        let boxProp =
            [ "BoundingBox"; "Box"; "Rect" ]
            |> List.tryPick (fun name ->
                let p = instance.GetType().GetProperty(name)
                if isNull p then None else Some p)

        match boxProp with
        | None -> None
        | Some p ->
            let v = p.GetValue(instance)
            if isNull v then None
            else
                let read names =
                    names
                    |> List.tryPick (fun n ->
                        let pp = v.GetType().GetProperty(n)
                        if isNull pp then None else Some(Convert.ToDouble(pp.GetValue(v))))
                let x = read [ "X"; "Left" ]
                let y = read [ "Y"; "Top" ]
                let w = read [ "Width"; "W" ]
                let h = read [ "Height"; "H" ]
                match x, y, w, h with
                | Some xx, Some yy, Some ww, Some hh ->
                    Some { X = xx; Y = yy; Width = ww; Height = hh }
                | _ -> None

    interface IObjectDetector with
        member _.DetectAsync(imagePath: string) = async {
            if not (File.Exists modelPath) then
                failwith $"YOLO model file was not found: {modelPath}"

            let yolo, yoloType = loadYolo()
            use _dispose =
                match yolo with
                | :? IDisposable as d -> d
                | _ -> null

            let methodInfo =
                [ "RunObjectDetection"; "Detect" ]
                |> List.tryPick (fun name ->
                    yoloType.GetMethods()
                    |> Array.tryFind (fun m ->
                        let parameters = m.GetParameters()
                        m.Name = name
                        && parameters.Length = 1
                        && parameters[0].ParameterType = typeof<string>))
                |> Option.defaultWith (fun _ -> failwith "No compatible detection method found in YoloDotNet.")

            let raw = methodInfo.Invoke(yolo, [| imagePath |])
            let detections =
                match raw with
                | :? System.Collections.IEnumerable as items ->
                    items
                    |> Seq.cast<obj>
                    |> Seq.toList
                | null -> []
                | single -> [ single ]

            return
                detections
                |> List.map (fun item ->
                    {
                        Label = tryGetString [ "Label"; "Name"; "Class" ] item |> Option.defaultValue "unknown"
                        Confidence = tryGetFloat "Confidence" item |> Option.defaultValue 0.0
                        Box = tryGetBoundingBox item
                    })
        }
