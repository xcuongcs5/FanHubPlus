from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams, PointStruct
from langchain_google_genai import GoogleGenerativeAIEmbeddings
from app.core.config import settings
import uuid

class VectorDBService:
    def __init__(self):
        self.client = QdrantClient(url=settings.QDRANT_URL)
        self.collection_name = settings.COLLECTION_NAME
        # Use Gemini's embedding model
        self.embeddings = GoogleGenerativeAIEmbeddings(
            model="models/gemini-embedding-001", transport="rest", 
            google_api_key=settings.GEMINI_API_KEY
        )
        self._ensure_collection()

    def _ensure_collection(self):
        # Gemini text-embedding-004 vector size is 768
        collections = self.client.get_collections().collections
        if not any(c.name == self.collection_name for c in collections):
            self.client.create_collection(
                collection_name=self.collection_name,
                vectors_config=VectorParams(size=3072, distance=Distance.COSINE),
            )

    def upsert_event(self, event_data: dict):
        event_id = event_data.get("eventId")
        title = event_data.get("title", "")
        description = event_data.get("description", "")
        
        # Tao tai lieu text de embedding
        content_to_embed = f"Event: {title}\nDescription: {description}"
        
        # Goi API Gemini de ma hoa thanh Vector
        vector = self.embeddings.embed_query(content_to_embed)
        
        # Luu vao Qdrant
        point = PointStruct(
            id=str(uuid.uuid5(uuid.NAMESPACE_DNS, event_id)) if event_id else str(uuid.uuid4()),
            vector=vector,
            payload={
                "event_id": event_id,
                "title": title,
                "description": description,
                "raw_content": content_to_embed
            }
        )
        
        self.client.upsert(
            collection_name=self.collection_name,
            points=[point]
        )
        print(f"[VectorDB] Upserted event {title} to Qdrant.")

vector_db = VectorDBService()
