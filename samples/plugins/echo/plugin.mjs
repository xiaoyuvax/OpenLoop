// 最小的 OpenLoop 插件：一行 JSON 进、一行 JSON 出（stdio）。
// 请求：{"jsonrpc":"2.0","id":1,"method":"tool.call","params":{"tool":"Echo","args":{"text":"..."}}}
// 应答：{"jsonrpc":"2.0","id":1,"result":{"ok":true,"data":"..."}}
import { createInterface } from 'node:readline';

const rl = createInterface({ input: process.stdin, crlfDelay: Infinity });

rl.on('line', line => {
  let request;
  try {
    request = JSON.parse(line);
  } catch {
    process.stderr.write('echo: 不是 JSON，已忽略\n');
    return;
  }

  const id = request?.id;
  if (id === undefined || id === null) return;

  if (request.method !== 'tool.call') {
    reply(id, { error: `未知方法：${request.method}` });
    return;
  }

  const tool = request.params?.tool;
  const args = request.params?.args ?? {};
  if (tool !== 'Echo') {
    reply(id, { error: `未知工具：${tool}` });
    return;
  }

  const text = typeof args.text === 'string' ? args.text : '';
  if (text === '__crash__') {
    process.stderr.write('echo: 按请求崩溃\n');
    process.exit(1);
  }
  if (text === '__sleep__') {
    setTimeout(() => reply(id, { result: { ok: true, data: 'late' } }), 5000);
    return;
  }

  reply(id, { result: { ok: true, data: text } });
});

rl.on('close', () => process.exit(0));

function reply(id, payload) {
  process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id, ...payload }) + '\n');
}
