# FanHub Media Streaming Service

**Stack:** Node.js / Express  
**Purpose:** High-throughput non-blocking media streaming via HTTP 206 Partial Content (Range Requests)  
**Storage:** Local disk / MinIO / S3

## Features
- Byte-range video trailer streaming with seekable playback
- Audio/Podcast/OST streaming pipeline
- Redis metadata caching for hot media

## Setup

```bash
npm install
npm run dev
```
