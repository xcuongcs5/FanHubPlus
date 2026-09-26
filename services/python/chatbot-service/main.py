import os
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from dotenv import load_dotenv

load_dotenv()

from app.services.rabbitmq_service import run_rabbitmq_listener_in_background
from app.api.chatbot_routes import router as chatbot_router

app = FastAPI(
    title="FanHub Chatbot API",
    description="RAG-based Chatbot Service using Gemini 1.5 Flash",
    version="1.0.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(chatbot_router, prefix="/api/v1/chatbot", tags=["Chatbot"])

@app.on_event("startup")
def startup_event():
    print("Starting background tasks...")
    run_rabbitmq_listener_in_background()

@app.get("/health")
def health_check():
    return {"status": "ok", "service": "chatbot-service"}

if __name__ == "__main__":
    import uvicorn
    port = int(os.getenv("PORT", 3005))
    uvicorn.run("main:app", host="0.0.0.0", port=port, reload=True)
