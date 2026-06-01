namespace AdvertisementAnalyzer.Refiners

open System

type LlamaJudge(modelPath: string option) =
    // Represents the llama.cpp model used for text reconciliation
    let hasLlm = false 
    
    member this.Reconcile(standardOcr: string, visualOcr: string, caption: string, language: string) =
        if hasLlm then
            // Call LLM (simulated here)
            standardOcr
        else
            this.HeuristicReconcile(standardOcr, visualOcr, caption)

    member private this.HeuristicReconcile(standardOcr: string, visualOcr: string, caption: string) =
        let sOcr = if isNull standardOcr then "" else standardOcr.Trim()
        let vOcr = if isNull visualOcr then "" else visualOcr.Trim()
        
        if String.IsNullOrEmpty sOcr then vOcr
        elif String.IsNullOrEmpty vOcr then sOcr
        elif vOcr.Contains(sOcr, StringComparison.OrdinalIgnoreCase) then vOcr
        elif sOcr.Contains(vOcr, StringComparison.OrdinalIgnoreCase) then sOcr
        else sOcr