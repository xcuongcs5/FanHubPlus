import uuid
import asyncio
import google.generativeai as genai
from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams, PointStruct
from langchain_core.embeddings import Embeddings
from app.core.config import settings

class GeminiRestEmbeddings(Embeddings):
    """
    Lightweight REST-based embeddings for Gemini.
    Avoids gRPC deadline exceeded issues inside Docker environments.
    """
    def __init__(self, model: str = "models/gemini-embedding-001", api_key: str = None):
        self.model = model
        if api_key:
            genai.configure(api_key=api_key)

    def embed_documents(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        try:
            res = genai.embed_content(model=self.model, content=texts)
            embeddings = res.get("embedding", [])
            if embeddings and isinstance(embeddings[0], list):
                return embeddings
            elif embeddings and isinstance(embeddings[0], (int, float)):
                return [embeddings]
            return [[0.0] * 3072 for _ in texts]
        except Exception as e:
            print(f"[GeminiRestEmbeddings] Error embed_documents: {e}")
            return [[0.0] * 3072 for _ in texts]

    def embed_query(self, text: str) -> list[float]:
        try:
            res = genai.embed_content(model=self.model, content=text)
            embedding = res.get("embedding", [])
            if embedding:
                return embedding
            return [0.0] * 3072
        except Exception as e:
            print(f"[GeminiRestEmbeddings] Error embed_query: {e}")
            return [0.0] * 3072

    async def aembed_query(self, text: str) -> list[float]:
        return await asyncio.to_thread(self.embed_query, text)

    async def aembed_documents(self, texts: list[str]) -> list[list[float]]:
        return await asyncio.to_thread(self.embed_documents, texts)

class VectorDBService:
    def __init__(self):
        self.client = QdrantClient(url=settings.QDRANT_URL)
        self.collection_name = settings.COLLECTION_NAME
        self.embeddings = GeminiRestEmbeddings(
            model="models/gemini-embedding-001",
            api_key=settings.GEMINI_API_KEY
        )
        self._ensure_collection()

    def _ensure_collection(self):
        try:
            collections = self.client.get_collections().collections
            if not any(c.name == self.collection_name for c in collections):
                self.client.create_collection(
                    collection_name=self.collection_name,
                    vectors_config=VectorParams(size=3072, distance=Distance.COSINE),
                )
                print(f"[VectorDB] Collection '{self.collection_name}' created successfully.")
        except Exception as e:
            print(f"[VectorDB] Error ensuring collection: {e}")

    def upsert_event(self, event_data: dict):
        try:
            event_id = event_data.get("eventId") or event_data.get("event_id")
            title = event_data.get("title", "")
            description = event_data.get("description", "")
            
            content_to_embed = f"Event: {title}\nDescription: {description}"
            vector = self.embeddings.embed_query(content_to_embed)
            
            point = PointStruct(
                id=str(uuid.uuid5(uuid.NAMESPACE_DNS, str(event_id))) if event_id else str(uuid.uuid4()),
                vector=vector,
                payload={
                    "event_id": str(event_id) if event_id else "",
                    "title": title,
                    "description": description,
                    "raw_content": content_to_embed
                }
            )
            
            self.client.upsert(
                collection_name=self.collection_name,
                points=[point]
            )
            print(f"[VectorDB] Upserted event '{title}' to Qdrant.")
        except Exception as e:
            print(f"[VectorDB] Error upserting event: {e}")

vector_db = VectorDBService()
