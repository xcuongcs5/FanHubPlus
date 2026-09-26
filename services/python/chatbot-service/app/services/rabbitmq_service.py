import pika
import json
import threading
from app.core.config import settings
from app.services.qdrant_service import vector_db

def callback(ch, method, properties, body):
    try:
        envelope = json.loads(body)
        payload = envelope.get("message", {})
        message_type = envelope.get("messageType", [])
        
        # Kiem tra loai su kien
        if any("EventChangedEvent" in t for t in message_type):
            print(f"[RabbitMQ] Processing EventChangedEvent for {payload.get('eventId')}")
            vector_db.upsert_event(payload)
            
        ch.basic_ack(delivery_tag=method.delivery_tag)
    except Exception as e:
        print(f"[RabbitMQ] Error processing message: {e}")
        # Tra lai hang doi neu gap loi nghiem trong (nack)
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

def start_consuming():
    try:
        parameters = pika.URLParameters(settings.RABBITMQ_URL)
        connection = pika.BlockingConnection(parameters)
        channel = connection.channel()
        
        queue_name = 'chatbot.sync.queue'
        channel.queue_declare(queue_name, durable=True)
        
        # Bind vao Exchange cua MassTransit tu .NET
        exchange_name = 'FanHub.Shared.Contracts.Events:EventChangedEvent'
        channel.exchange_declare(exchange=exchange_name, exchange_type='fanout', durable=True)
        channel.queue_bind(exchange=exchange_name, queue=queue_name)
        
        channel.basic_consume(queue=queue_name, on_message_callback=callback, auto_ack=False)
        
        print("[RabbitMQ] Started listening for EventChangedEvent...")
        channel.start_consuming()
    except Exception as e:
        print(f"[RabbitMQ] Connection failed: {e}")

def run_rabbitmq_listener_in_background():
    thread = threading.Thread(target=start_consuming, daemon=True)
    thread.start()
