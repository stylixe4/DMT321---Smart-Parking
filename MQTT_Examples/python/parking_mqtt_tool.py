"""
Smart Parking Twin - เครื่องมือทดสอบ MQTT (Python)

ติดตั้ง:   pip install paho-mqtt
ตัวอย่าง:
  python parking_mqtt_tool.py monitor                 # ดูสถานะที่ Unity ส่งออกมา
  python parking_mqtt_tool.py cmd "spawn 3"           # สั่งเสกรถลงช่อง 3
  python parking_mqtt_tool.py cmd '{"cmd":"speed","value":10}'
  python parking_mqtt_tool.py sensor 2 1              # จำลองเซนเซอร์ช่อง 2 ว่ามีรถ (0 = ว่าง)
  python parking_mqtt_tool.py simulate                # สุ่มรถเข้า-ออกช่อง 1-4 เรื่อยๆ (Ctrl+C หยุด)

ตัวเลือก: --host broker.hivemq.com --port 1883 --root dmt321/smartparking --device PARK-01
"""
import argparse
import json
import random
import sys
import time

import paho.mqtt.client as mqtt


def make_client(client_id):
    # รองรับทั้ง paho-mqtt 2.x และ 1.x
    try:
        return mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=client_id)
    except AttributeError:
        return mqtt.Client(client_id=client_id)


def connect(args, on_message=None, subscribe=None):
    client = make_client(f"parking-tool-{random.randint(1000, 9999)}")
    if args.username:
        client.username_pw_set(args.username, args.password)
    if args.tls:
        client.tls_set()

    def on_connect(c, userdata, flags, reason_code, properties=None):
        print(f"[MQTT] connected to {args.host}:{args.port}")
        for topic in subscribe or []:
            c.subscribe(topic)
            print(f"[MQTT] subscribed {topic}")

    client.on_connect = on_connect
    if on_message:
        client.on_message = on_message
    client.connect(args.host, args.port, keepalive=30)
    return client


def print_message(client, userdata, msg):
    text = msg.payload.decode("utf-8", errors="replace")
    topic = msg.topic.split("/", 3)[-1] if msg.topic.count("/") >= 3 else msg.topic
    if msg.topic.endswith("/status"):
        try:
            s = json.loads(text)
            busy = [str(x["slot"]) + ("!" if x["overstay"] else "") for x in s["slots"] if x["occupied"]]
            print(f"[status] free {s['freeSlots']}/{s['totalSlots']} ({s['availabilityPercent']}%)"
                  f"  occupied: {', '.join(busy) or '-'}  x{s['timeScale']}  warning: {s['warningMessage']}")
            return
        except (ValueError, KeyError):
            pass
    print(f"[{topic}] {text}")


def publish_once(args, topic, payload):
    client = connect(args)
    client.loop_start()
    time.sleep(1.0)
    info = client.publish(topic, payload, qos=0, retain=False)
    info.wait_for_publish()
    print(f"[MQTT] {topic} <- {payload}")
    time.sleep(0.5)
    client.disconnect()
    client.loop_stop()


def main():
    p = argparse.ArgumentParser(description="Smart Parking Twin MQTT tool")
    p.add_argument("mode", choices=["monitor", "cmd", "sensor", "simulate"])
    p.add_argument("args", nargs="*")
    p.add_argument("--host", default="broker.hivemq.com")
    p.add_argument("--port", type=int, default=1883)
    p.add_argument("--tls", action="store_true")
    p.add_argument("--username")
    p.add_argument("--password")
    p.add_argument("--root", default="dmt321/smartparking")
    p.add_argument("--device", default="PARK-01")
    args = p.parse_args()
    base = f"{args.root.rstrip('/')}/{args.device}"

    if args.mode == "monitor":
        client = connect(args, print_message, [f"{base}/status", f"{base}/ack", f"{base}/online"])
        client.loop_forever()

    elif args.mode == "cmd":
        if not args.args:
            sys.exit('ต้องระบุคำสั่ง เช่น  cmd "spawn 3"')
        publish_once(args, f"{base}/cmd", " ".join(args.args))

    elif args.mode == "sensor":
        if len(args.args) != 2:
            sys.exit("ใช้แบบนี้:  sensor <ช่อง 1-7> <1|0>")
        publish_once(args, f"{base}/sensor/{args.args[0]}", args.args[1])

    elif args.mode == "simulate":
        client = connect(args, print_message, [f"{base}/ack"])
        client.loop_start()
        state = {n: False for n in range(1, 5)}
        print("จำลองรถเข้า-ออกช่อง 1-4 ทุก 3-8 วินาที (Ctrl+C เพื่อหยุด)")
        try:
            while True:
                time.sleep(random.uniform(3, 8))
                slot = random.choice(list(state))
                state[slot] = not state[slot]
                client.publish(f"{base}/sensor/{slot}", "1" if state[slot] else "0")
                print(f"[sensor] slot {slot} = {'มีรถ' if state[slot] else 'ว่าง'}")
        except KeyboardInterrupt:
            for slot in state:
                client.publish(f"{base}/sensor/{slot}", "0")
            time.sleep(0.5)
            client.disconnect()
            client.loop_stop()


if __name__ == "__main__":
    main()
