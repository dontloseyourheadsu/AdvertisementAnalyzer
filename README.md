# Street Advertisement Analyzer (F#)

This is the F# implementation of the Street Advertisement Analyzer, replicated from the original Python architecture.

## Getting Started

### 1. Build and Run with Docker

The easiest way to run the analyzer is using Docker Compose.

#### A. Download the Dataset into the Docker Volume
The dataset is stored in a persistent Docker volume (`dataset-volume`) to keep the host clean. You can download the dataset into the volume using one of the following commands:

* **Zenodo** (Free, no credentials needed):
  ```bash
  docker compose run fsharp-analyzer download --source zenodo --target /app/dataset
  ```

* **Kaggle** (Requires `KAGGLE_API_TOKEN` in your environment or `.env` file):
  ```bash
  docker compose run fsharp-analyzer download --source kaggle --target /app/dataset
  ```

* **Roboflow** (Requires `ROBOFLOW_API_KEY` in your environment or `.env` file):
  ```bash
  docker compose run fsharp-analyzer download --source roboflow --target /app/dataset --api-key YOUR_API_KEY
  ```

#### B. Run the Analysis Pipeline
Once the dataset is downloaded, run the pipeline:
```bash
docker compose up --build
```

This will:
1. Build the F# application and install all necessary dependencies (OpenCV, Tesseract, Python, and Kagglehub).
2. Run the analysis pipeline on the images in the persistent Docker volume `/app/dataset`.
3. Save the results (annotated images and `analysis_report.csv`) to the host's `output/` directory.


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
