from langchain_google_genai import ChatGoogleGenerativeAI
from langchain.prompts import ChatPromptTemplate
from app.core.config import settings
from app.services.qdrant_service import vector_db

class LLMService:
    def __init__(self):
        self.llm = ChatGoogleGenerativeAI(
            model="gemini-flash-latest", 
            google_api_key=settings.GEMINI_API_KEY,
            temperature=0.3
        )
        self.router_llm = ChatGoogleGenerativeAI(
            model="gemini-flash-latest", 
            google_api_key=settings.GEMINI_API_KEY,
            temperature=0.0
        )

    async def check_intent(self, user_message: str) -> bool:
        router_prompt = ChatPromptTemplate.from_messages([
            ("system", "Ban la mot bo loc an toan. Neu cau hoi lien quan den am nhac, nghe si, than tuong (idol), anime, mua ve, hang hoa luu niem, hoac nen tang FanHubPlus, hay tra loi 'YES'. Neu la cac chu de ngoai le, tra loi 'NO'."),
            ("human", "{question}")
        ])
        
        chain = router_prompt | self.router_llm
        response = await chain.ainvoke({"question": user_message})
        return "YES" in response.content.upper()

    async def ask(self, user_message: str) -> str:
        is_valid_intent = await self.check_intent(user_message)
        if not is_valid_intent:
            return "Xin loi, minh la tro ly ao cua FanHubPlus nen minh chi co the ho tro ban cac van de ve Su kien, Nghe si, Mua ve va nen tang FanHub ma thoi nhe!"

        query_vector = await vector_db.embeddings.aembed_query(user_message)
        
        search_result = vector_db.client.search(
            collection_name=vector_db.collection_name,
            query_vector=query_vector,
            limit=3
        )
        
        context_texts = []
        for hit in search_result:
            if hit.score > 0.7:
                context_texts.append(hit.payload.get("raw_content", ""))
        
        context_string = "\n\n".join(context_texts) if context_texts else "Khong tim thay thong tin su kien nao tren he thong."

        rag_prompt = ChatPromptTemplate.from_messages([
            ("system", """Ban la FanHub AI, tro ly ao chinh thuc cua FanHubPlus. 
Tuyet doi tu choi tra loi neu cau hoi nam ngoai chu de Su kien/Idol.
Hay tra loi tu nhien, than thien, va CHi dua tren Context (Du lieu tu database) ma toi cung cap duoi day.
Neu Context ghi la khong co, hay noi la khong tim thay thong tin, khong duoc tu bia ra.

[CONTEXT BÃƒÂ¡Ã‚ÂºÃ‚Â®T Ãƒâ€žÃ‚Â ÃƒÂ¡Ã‚ÂºÃ‚Â¦U]
{context}
[CONTEXT KÃƒÂ¡Ã‚ÂºÃ‚Â¾T THÃƒÆ’Ã…Â¡C]"""),
            ("human", "{question}")
        ])
        
        chain = rag_prompt | self.llm
        response = await chain.ainvoke({
            "context": context_string,
            "question": user_message
        })
        
        return response.content

llm_service = LLMService()