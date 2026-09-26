from fastapi import APIRouter
from pydantic import BaseModel
from app.core.llm_service import llm_service

router = APIRouter()

class ChatRequest(BaseModel):
    message: str
    session_id: str = "default_session"

class ChatResponse(BaseModel):
    reply: str

@router.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest):
    reply_text = await llm_service.ask(request.message)
    return ChatResponse(reply=reply_text)