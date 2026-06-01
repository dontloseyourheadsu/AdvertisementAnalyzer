namespace AdvertisementAnalyzer.Core

open System
open System.IO
open System.Reflection

type DetectedObject = {
    Label: string
    Confidence: float
    Box: Rect option
}
and Rect = { X: float; Y: float; Width: float; Height: float }

// This adapter uses reflection so it can tolerate minor YoloDotNet API evolution.
type YoloDetector(modelPath: string) =
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

        // Load CpuExecutionProvider from its own assembly if needed
        let providerAsm = 
            AppDomain.CurrentDomain.GetAssemblies()
            |> Array.tryFind (fun a -> a.GetName().Name = "YoloDotNet.ExecutionProvider.Cpu")
            |> Option.defaultWith (fun _ -> Assembly.Load("YoloDotNet.ExecutionProvider.Cpu"))
            
        let providerType = 
            providerAsm.GetType("YoloDotNet.ExecutionProvider.Cpu.CpuExecutionProvider")
            |> Option.ofObj
            |> Option.defaultWith (fun _ -> failwith "CpuExecutionProvider type was not found.")

        // Create provider instance with modelPath
        let providerCtor = 
            providerType.GetConstructors() 
            |> Array.tryFind (fun c -> 
                let p = c.GetParameters()
                p.Length >= 1 && p.[0].ParameterType = typeof<string>)
            |> Option.defaultWith (fun _ -> failwith "CpuExecutionProvider constructor not found.")
        
        let provider = providerCtor.Invoke([| modelPath |])

        // Create options and set provider
        let options = Activator.CreateInstance(optionsType)
        let providerProp = optionsType.GetProperty("ExecutionProvider")
        if not (isNull providerProp) then
            providerProp.SetValue(options, provider)

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
                p.Length = 1 && p.[0].ParameterType = optionsType)
            |> Option.defaultWith (fun _ -> failwith "Compatible Yolo constructor not found.")

        let instance = ctor.Invoke([| options |])
        if isNull instance then failwith "Failed to create YOLO instance."
        instance, yoloType

    let tryGetFloat (name: string) (instance: obj) =
        if isNull instance then None else
        let prop = instance.GetType().GetProperty(name)
        if isNull prop then None
        else
            let value = prop.GetValue(instance)
            if isNull value then None else Some(Convert.ToDouble(value))

    let tryGetString (names: string list) (instance: obj) =
        if isNull instance then None else
        names
        |> List.tryPick (fun name ->
            let prop = instance.GetType().GetProperty(name)
            if isNull prop then None
            else
                let value = prop.GetValue(instance)
                if isNull value then None else Some(string value))

    let tryGetBoundingBox (instance: obj) =
        if isNull instance then None else
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

    member _.Detect(imagePath: string) =
        if not (File.Exists imagePath) then
            failwithf "Image file was not found: %s" imagePath

        let yolo, yoloType = loadYolo()
        use _dispose =
            match yolo with
            | :? IDisposable as d -> d
            | _ -> null

        // Load image as SKBitmap since YoloDotNet doesn't take string path directly in this version
        use bitmap = SkiaSharp.SKBitmap.Decode(imagePath)
        if isNull bitmap then failwithf "Failed to decode image: %s" imagePath

        printfn "  [DEBUG] Finding RunObjectDetection method..."
        let methodInfo =
            [ "RunObjectDetection"; "Detect" ]
            |> List.tryPick (fun name ->
                yoloType.GetMethods()
                |> Array.tryFind (fun m ->
                    let parameters = m.GetParameters()
                    m.Name = name
                    && parameters.Length >= 1
                    && (parameters.[0].ParameterType.Name.Contains("SKBitmap") || parameters.[0].ParameterType.Name.Contains("Bitmap"))))
            |> Option.defaultWith (fun _ -> failwith "No compatible detection method found in YoloDotNet.")

        printfn "  [DEBUG] Invoking RunObjectDetection..."
        // Invoke with bitmap and explicit parameters
        let args : obj array = 
            let parameters = methodInfo.GetParameters()
            let res = Array.create parameters.Length null
            res.[0] <- bitmap :> obj
            
            // Try to set confidence and iou if they exist in parameters
            for i in 1 .. parameters.Length - 1 do
                let p = parameters.[i]
                if p.Name.Contains("confidence", StringComparison.OrdinalIgnoreCase) then
                    res.[i] <- 0.25 :> obj
                elif p.Name.Contains("iou", StringComparison.OrdinalIgnoreCase) then
                    res.[i] <- 0.45 :> obj
                elif p.ParameterType.IsValueType then
                    res.[i] <- Activator.CreateInstance(p.ParameterType)
                else
                    res.[i] <- null
            res
        
        let raw = methodInfo.Invoke(yolo, args)
        printfn "  [DEBUG] Parsing results..."
        let detections =
            match raw with
            | :? System.Collections.IEnumerable as items ->
                items
                |> Seq.cast<obj>
                |> Seq.toList
            | null -> []
            | single -> [ single ]

        detections
        |> List.map (fun item ->
            {
                Label = tryGetString [ "Label"; "Name"; "Class" ] item |> Option.defaultValue "unknown"
                Confidence = tryGetFloat "Confidence" item |> Option.defaultValue 0.0
                Box = tryGetBoundingBox item
            })
