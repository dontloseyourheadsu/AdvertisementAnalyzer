# Street Advertisement Analyzer (F#)

This is the F# implementation of the Street Advertisement Analyzer, replicated from the original Python architecture.

## Getting Started

### 1. Build and Run with Docker

The easiest way to run the analyzer is using Docker Compose.

```bash
docker compose up --build
```

This will:
1. Build the F# application using the .NET 10 SDK.
2. Install all necessary native dependencies (OpenCV, Tesseract, etc.).
3. Run the analysis pipeline on the images in `data/dataset/`.
4. Save the results (annotated images and `analysis_report.csv`) to the `output/` directory.

### 2. Project Structure

- **`src/Core/Processor.fs`**: Image preprocessing, CV fallback detection, and PaddleOCR clustering.
- **`src/Core/Pipeline.fs`**: Orchestration of the entire pipeline.
- **`src/Core/YoloDetector.fs`**: Reflection-based wrapper for YOLOv8 inference.
- **`src/Refiners/`**: Modules for Florence-2 (Vision OCR) and Gemma (LLM Judge) logic.
- **`src/Cli/Cli.fs`**: Command-line interface logic.

### 3. Model Details

- **Detection**: YOLOv8n (Custom), exported to ONNX format for compatibility with `YoloDotNet`.
- **OCR**: PaddleOCR (via `Sdcb.PaddleOCR`).
- **Clustering**: DBSCAN-based line grouping for structured text extraction.

## License

This project is licensed under the same terms as the original repository.
