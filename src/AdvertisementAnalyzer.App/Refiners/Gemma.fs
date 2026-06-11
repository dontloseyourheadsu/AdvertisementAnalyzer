namespace AdvertisementAnalyzer.Refiners

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Net.Http
open System.Text

type AdDetails = {
    [<JsonPropertyName("brand")>] Brand: string
    [<JsonPropertyName("category")>] Category: string
    [<JsonPropertyName("structure_type")>] StructureType: string
    [<JsonPropertyName("text_content")>] TextContent: string
    [<JsonPropertyName("items")>] Items: string[]
}

type BrandEntry = {
    [<JsonPropertyName("name")>] Name: string
    [<JsonPropertyName("category")>] Category: string
    [<JsonPropertyName("keywords")>] Keywords: string[]
}

type LlamaJudge(modelPath: string option) =
    let ollamaUrl = "http://localhost:11434/api/generate"
    let modelName = "gemma4:e4b"

    let brands =
        try
            let path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mexican_brands.json")
            if File.Exists(path) then
                let json = File.ReadAllText(path)
                JsonSerializer.Deserialize<BrandEntry[]>(json)
            else
                [||]
        with ex ->
            printfn "Failed to load brands database: %s" ex.Message
            [||]

    member private this.HeuristicAnalyze(standardOcr: string, visualOcr: string, caption: string) : AdDetails =
        let ocrLower = (standardOcr + " " + visualOcr).ToLowerInvariant()
        let captionLower = caption.ToLowerInvariant()
        let combined = ocrLower + " " + captionLower

        // 1. Brand & Category Detection from Database
        let mutable brand = "Generico"
        let mutable category = "Otros"

        let foundEntry = 
            brands 
            |> Array.tryFind (fun entry ->
                entry.Keywords 
                |> Array.exists (fun kw -> combined.Contains(kw.ToLowerInvariant()))
            )

        match foundEntry with
        | Some entry ->
            brand <- entry.Name
            category <- entry.Category
        | None ->
            // Category fallback if no specific brand matched
            if combined.Contains("red") || combined.Contains("conecta") || combined.Contains("megas") || combined.Contains("gigas") || combined.Contains("celular") || combined.Contains("prepago") then
                category <- "Telecomunicaciones"
            elif combined.Contains("beer") || combined.Contains("cerveza") || combined.Contains("refresco") || combined.Contains("sabor") || combined.Contains("bebida") then
                category <- "Bebidas"
            elif combined.Contains("pan") || combined.Contains("pastel") || combined.Contains("queso") || combined.Contains("leche") || combined.Contains("comida") || combined.Contains("hamburguesa") || combined.Contains("pizza") || combined.Contains("alimento") then
                category <- "Alimentos"
            elif combined.Contains("banco") || combined.Contains("crédito") || combined.Contains("dinero") || combined.Contains("préstamo") || combined.Contains("tarjeta") || combined.Contains("finanzas") then
                category <- "Servicios Financieros"
            elif combined.Contains("tienda") || combined.Contains("super") || combined.Contains("comercio") || combined.Contains("retail") then
                category <- "Comercio"
            elif combined.Contains("carro") || combined.Contains("auto") || combined.Contains("llanta") || combined.Contains("gasolina") || combined.Contains("automotriz") then
                category <- "Automotriz"


        // 3. Structure Type Detection
        let structureType =
            if combined.Contains("barda") || combined.Contains("muro") || combined.Contains("wall") || combined.Contains("painted wall") || combined.Contains("pinta") then
                "Barda"
            elif combined.Contains("parabús") || combined.Contains("bus shelter") || combined.Contains("bus stop") || combined.Contains("parada") || combined.Contains("mupi") then
                "Parabús"
            elif combined.Contains("espectacular") || combined.Contains("unipolo") || combined.Contains("unipolar") || combined.Contains("billboard") || combined.Contains("highway sign") || combined.Contains("large sign") then
                "Espectacular"
            elif combined.Contains("pantalla") || combined.Contains("digital") || combined.Contains("led") || combined.Contains("screen") then
                "Pantalla Digital"
            elif combined.Contains("lona") || combined.Contains("banner") || combined.Contains("pendón") || combined.Contains("colgado") then
                "Lona"
            elif combined.Contains("fachada") || combined.Contains("tienda") || combined.Contains("anuncio luminoso") || combined.Contains("storefront") || combined.Contains("letrero") then
                "Fachada"
            else
                "Espectacular"

        // 4. Cleaned Text Content
        let textContent = 
            if String.IsNullOrWhiteSpace standardOcr then visualOcr else standardOcr

        // 5. Items list
        let items = 
            [
                if combined.Contains("bottle") || combined.Contains("botella") then yield "botella"
                if combined.Contains("smartphone") || combined.Contains("cellphone") || combined.Contains("celular") || combined.Contains("teléfono") then yield "celular"
                if combined.Contains("car") || combined.Contains("auto") || combined.Contains("coche") || combined.Contains("vehículo") then yield "automóvil"
                if combined.Contains("person") || combined.Contains("people") || combined.Contains("persona") || combined.Contains("hombre") || combined.Contains("mujer") then yield "persona"
                if combined.Contains("logo") then yield "logotipo"
                if combined.Contains("beer") || combined.Contains("cerveza") then yield "cerveza"
                if combined.Contains("can") || combined.Contains("lata") then yield "lata"
                if combined.Contains("bread") || combined.Contains("pan") then yield "pan"
            ] |> List.toArray

        { Brand = brand; Category = category; StructureType = structureType; TextContent = textContent.Trim(); Items = items }

    member this.Reconcile(standardOcr: string, visualOcr: string, caption: string, language: string) : AdDetails =
        try
            use client = new HttpClient()
            client.Timeout <- TimeSpan.FromSeconds(15.0)

            let prompt = 
                sprintf "Analiza los datos de este anuncio exterior en México:\n- Texto OCR detectado: \"%s\"\n- OCR Visual: \"%s\"\n- Descripción de la imagen (VLM): \"%s\"\n\nExtrae y devuelve un objeto JSON válido con los siguientes campos en español (no inventes datos si no los detectas):\n{\n  \"brand\": \"Marca del anuncio (ej. Coca-Cola, Telcel, Corona, OXXO, o 'Generico')\",\n  \"category\": \"Categoría (Bebidas, Alimentos, Telecomunicaciones, Finanzas, Comercio, Automotriz, etc.)\",\n  \"structure_type\": \"Tipo de estructura (Espectacular, Parabús, Barda, Pantalla Digital, Fachada, Lona)\",\n  \"text_content\": \"Texto principal legible en el anuncio\",\n  \"items\": [\"Lista\", \"de\", \"objetos\", \"visibles\"]\n}\nDevuelve exclusivamente el JSON sin explicaciones ni formato markdown."
                    standardOcr visualOcr caption

            let requestBody = 
                sprintf "{\"model\": \"%s\", \"prompt\": %s, \"format\": \"json\", \"stream\": false}"
                    modelName
                    (JsonSerializer.Serialize(prompt))

            use content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            let response = client.PostAsync(ollamaUrl, content) |> Async.AwaitTask |> Async.RunSynchronously
            
            if response.IsSuccessStatusCode then
                let jsonResponse = response.Content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
                use doc = JsonDocument.Parse(jsonResponse)
                let root = doc.RootElement
                if root.TryGetProperty("response") |> fst then
                    let rawModelResponse = root.GetProperty("response").GetString()
                    try
                        JsonSerializer.Deserialize<AdDetails>(rawModelResponse)
                    with _ ->
                        this.HeuristicAnalyze(standardOcr, visualOcr, caption)
                else
                    this.HeuristicAnalyze(standardOcr, visualOcr, caption)
            else
                this.HeuristicAnalyze(standardOcr, visualOcr, caption)
        with _ ->
            this.HeuristicAnalyze(standardOcr, visualOcr, caption)