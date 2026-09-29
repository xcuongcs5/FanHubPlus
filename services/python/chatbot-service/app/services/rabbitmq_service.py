import pika
import json
import threading
import time
from urllib.parse import urlparse, unquote
from app.core.config import settings
from app.services.qdrant_service import vector_db

def callback(ch, method, properties, body):
    try:
        envelope = json.loads(body)
        payload = envelope.get("message", {})
        message_type = envelope.get("messageType", [])
        
        # Check event type
        if any("EventChangedEvent" in t for t in message_type):
            print(f"[RabbitMQ] Processing EventChangedEvent for {payload.get('eventId')}")
            vector_db.upsert_event(payload)
            
        ch.basic_ack(delivery_tag=method.delivery_tag)
    except Exception as e:
        print(f"[RabbitMQ] Error processing message: {e}")
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

def get_connection_parameters():
    url = settings.RABBITMQ_URL
    try:
        parsed = urlparse(url)
        raw_path = parsed.path.lstrip('/')
        vhost = unquote(raw_path) if raw_path else 'fanhub'
        user = unquote(parsed.username) if parsed.username else 'fanhub'
        password = unquote(parsed.password) if parsed.password else 'guest'
        host = parsed.hostname or 'rabbitmq'
        port = parsed.port or 5672

        credentials = pika.PlainCredentials(user, password)
        return pika.ConnectionParameters(
            host=host,
            port=port,
            virtual_host=vhost,
            credentials=credentials,
            heartbeat=60,
            blocked_connection_timeout=300
        )
    except Exception as e:
        print(f"[RabbitMQ] Fallback to URLParameters due to parse error: {e}")
        return pika.URLParameters(url)

def start_consuming():
    while True:
        try:
            parameters = get_connection_parameters()
            connection = pika.BlockingConnection(parameters)
            channel = connection.channel()
            
            queue_name = 'chatbot.sync.queue'
            channel.queue_declare(queue_name, durable=True)
            
            # Bind to MassTransit exchange from .NET EventService
            exchange_name = 'FanHub.Shared.Contracts.Events:EventChangedEvent'
            channel.exchange_declare(exchange=exchange_name, exchange_type='fanout', durable=True)
            channel.queue_bind(exchange=exchange_name, queue=queue_name)
            
            channel.basic_consume(queue=queue_name, on_message_callback=callback, auto_ack=False)
            
            print("[RabbitMQ] Started listening for EventChangedEvent...")
            channel.start_consuming()
        except Exception as e:
            print(f"[RabbitMQ] Connection failed or disconnected: {e}. Retrying in 5 seconds...")
            time.sleep(5)

def run_rabbitmq_listener_in_background():
    thread = threading.Thread(target=start_consuming, daemon=True)
    thread.start()
