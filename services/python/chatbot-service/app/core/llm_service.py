import asyncio
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain.prompts import ChatPromptTemplate
from app.core.config import settings
from app.services.qdrant_service import vector_db

class LLMService:
    def __init__(self):
        # Models with fresh quota and fast response times
        self.candidate_models = [
            "gemini-3.5-flash",
            "gemini-3.5-flash-lite",
            "gemini-3.1-flash-lite",
            "gemini-3.1-flash-lite-preview"
        ]
        self.llm_instances = {
            m: ChatGoogleGenerativeAI(
                model=m,
                google_api_key=settings.GEMINI_API_KEY,
                transport="rest",
                temperature=0.3,
                max_retries=0
            )
            for m in self.candidate_models
        }

    async def _execute_with_model_fallback(self, prompt_template: ChatPromptTemplate, inputs: dict) -> str:
        last_exception = None
        for model_name in self.candidate_models:
            try:
                llm = self.llm_instances[model_name]
                chain = prompt_template | llm
                response = await asyncio.to_thread(chain.invoke, inputs)
                return response.content
            except Exception as e:
                print(f"[LLMService] Model '{model_name}' encounter error: {e}. Trying next fallback model...")
                last_exception = e
                continue
        raise last_exception or Exception("All candidate LLM models failed.")

    async def ask(self, user_message: str) -> str:
        try:
            # 1. Search Vector DB for context
            context_string = ""
            try:
                query_vector = await vector_db.embeddings.aembed_query(user_message)
                search_result = vector_db.client.search(
                    collection_name=vector_db.collection_name,
                    query_vector=query_vector,
                    limit=3
                )
                
                context_texts = []
                for hit in search_result:
                    if hit.score > 0.45:
                        raw_content = hit.payload.get("raw_content", "")
                        if raw_content:
                            context_texts.append(raw_content)
                
                if context_texts:
                    context_string = "\n\n".join(context_texts)
            except Exception as e:
                print(f"[LLMService] Vector search error (degraded to direct LLM): {e}")

            if not context_string:
                context_string = "Chưa có sự kiện cụ thể nào trong cơ sở dữ liệu hệ thống."

            # 2. RAG Generation with Automatic Model Fallback
            rag_prompt = ChatPromptTemplate.from_messages([
                ("system", """Bạn là FanHub AI, trợ lý ảo thông minh và thân thiện của nền tảng FanHubPlus (nền tảng kết nối người hâm mộ, sự kiện âm nhạc, idol, concert, vé và merchandise).
- Hãy trả lời tự nhiên, nhiệt tình, lịch sự bằng tiếng Việt.
- Nếu người dùng chào hỏi, hãy chào lại thân thiện và giới thiệu ngắn gọn các dịch vụ bạn có thể hỗ trợ trên FanHubPlus.
- Nếu người dùng hỏi chủ đề hoàn toàn không liên quan (chính trị nhạy cảm, bài tập toán học, code phần mềm...), hãy lịch sự từ chối và nhắc người dùng rằng FanHub AI chuyên hỗ trợ các sự kiện giải trí và thần tượng.
- Nếu trong [DỮ LIỆU SỰ KIỆN] có thông tin liên quan đến câu hỏi, hãy ưu tiên dùng thông tin đó để trả lời chi tiết, chính xác.
- Nếu không có sự kiện cụ thể trong dữ liệu, hãy cung cấp câu trả lời hữu ích, gợi ý người dùng tìm kiếm trên ứng dụng FanHubPlus hoặc tư vấn thông tin fandom phổ biến mà không bịa đặt thông tin sai lệch về vé.

[DỮ LIỆU SỰ KIỆN TỪ HỆ THỐNG]:
{context}
[HẾT DỮ LIỆU]"""),
                ("human", "{question}")
            ])
            
            return await self._execute_with_model_fallback(rag_prompt, {
                "context": context_string,
                "question": user_message
            })

        except Exception as e:
            print(f"[LLMService] All LLM models failed in ask(): {e}")
            err_str = str(e)
            if "429" in err_str or "Quota exceeded" in err_str or "TooManyRequests" in err_str:
                return "Hệ thống AI hiện đang tạm thời đạt giới hạn lượt hỏi trong ngày của gói thử nghiệm (Gemini Quota). Bạn vui lòng thử lại sau một lát hoặc kiểm tra hạn ngạch API nhé!"
            return "Xin lỗi bạn, trợ lý AI hiện đang tạm thời bận xử lý hoặc gặp gián đoạn kết nối. Bạn vui lòng thử lại sau giây lát nhé!"

llm_service = LLMService()