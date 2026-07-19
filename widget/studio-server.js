// Tiny static server for SceneBridge Studio (no dependencies).
const http = require('http');
const fs = require('fs');
const path = require('path');
const ROOT = __dirname;
const PORT = 8791;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.css': 'text/css' };
http.createServer((req, res) => {
  let p = req.url.split('?')[0];
  if (p === '/') p = '/studio.html';
  const file = path.join(ROOT, path.normalize(p).replace(/^([.][.][/\\])+/, ''));
  // Must stay inside ROOT. Guard the path-SEPARATOR boundary, not just a string prefix, so a
  // sibling directory like "<root>-secret" can never pass.
  if (file !== ROOT && !file.startsWith(ROOT + path.sep)) { res.writeHead(403); res.end('forbidden'); return; }
  // statSync can throw (TOCTOU delete, locked file) -- never let that crash the whole server.
  let stat;
  try { stat = fs.statSync(file); } catch { res.writeHead(404); res.end('not found'); return; }
  if (stat.isDirectory()) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'Content-Type': MIME[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  const stream = fs.createReadStream(file);
  stream.on('error', () => { if (!res.headersSent) res.writeHead(500); res.end('read error'); });
  stream.pipe(res);
}).listen(PORT, '127.0.0.1', () => console.log('Studio at http://localhost:' + PORT));
