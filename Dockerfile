# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy fsproj and restore
COPY src/AdvertisementAnalyzer.App/*.fsproj ./AdvertisementAnalyzer.App/
RUN dotnet restore ./AdvertisementAnalyzer.App/AdvertisementAnalyzer.App.fsproj

# Copy everything else and build
COPY src/AdvertisementAnalyzer.App/ ./AdvertisementAnalyzer.App/
RUN dotnet publish ./AdvertisementAnalyzer.App/AdvertisementAnalyzer.App.fsproj -c Release -o /app/publish -r linux-x64 --self-contained false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Install native dependencies for OpenCV, PaddleOCR, and Python
RUN apt-get update && apt-get install -y --no-install-recommends \
    libglib2.0-0 \
    libgl1 \
    libgomp1 \
    libsm6 \
    libxrender1 \
    libxext6 \
    libtesseract5 \
    tesseract-ocr \
    libopencv-dev \
    libgtk-3-0 \
    libatk1.0-0 \
    python3 \
    python3-pip \
    && rm -rf /var/lib/apt/lists/*

# Install python dependencies
RUN pip3 install --no-cache-dir --break-system-packages kagglehub

COPY --from=build /app/publish .

# Copy python script
COPY src/download_kaggle.py ./src/

# The application expects some models and datasets to be mounted or present.
# We'll use the entry point to run the app.
ENTRYPOINT ["dotnet", "AdvertisementAnalyzer.App.dll"]

