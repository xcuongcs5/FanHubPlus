# FanHub AI Chatbot Service

**Stack:** Python 3 / FastAPI / LangChain  
**Purpose:** AI-powered chatbot assistant for FanHub platform FAQ, content recommendation, and multi-step conversational flows (SRS 1.6).

## Features
- RAG (Retrieval-Augmented Generation) for answering platform FAQs.
- Conversational content recommendations based on visitor preferences.
- Context-aware chat history tracking.

## Setup
```bash
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8000
```
