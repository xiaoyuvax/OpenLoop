import json, io, sys
sys.stdout.reconfigure(encoding='utf-8')
BASE = r'C:\Users\Xiaoyu\AppData\Roaming\OpenLoop\chats'
for name in ['d586500d53a71455.json', '8b98ee27135a9d7c.json']:
    d = json.load(io.open(BASE + '\\' + name, encoding='utf-8'))
    print('==', name)
    for i, e in enumerate(d):
        msgs = e.get('messages') or []
        sz = sum(len(m.get('content') or '') for m in msgs)
        a = e.get('assistant') or ''
        tag = 'FAIL' if 'free-limit' in a else ('STOP' if a.startswith('（已停止') else ('OK' if a else 'EMPTY'))
        print(f'{i:3} {tag:5} msgs={len(msgs):4} chars={sz:9} steps={e.get("steps",0):4} ms={e.get("ms",0)}')
