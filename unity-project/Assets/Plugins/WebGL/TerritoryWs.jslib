// Passes the browser WebSocket API through to Unity. The socket lives in JS;
// C# is notified through SendMessage callbacks plus an optional poll buffer.
mergeInto(LibraryManager.library, {
  TerritoryWsConnect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (typeof window === 'undefined') return;
    if (window.__territoryWs) { try { window.__territoryWs.close(); } catch (e) {} }
    var queue = [];
    window.__territoryWsQueue = queue;
    var ws = new WebSocket(url);
    window.__territoryWs = ws;
    var target = 'TerritoryWs';
    ws.onopen = function () {
      if (typeof SendMessage === 'function') {
        try { SendMessage(target, 'OnWsOpen', ''); } catch (e) {}
      }
    };
    ws.onmessage = function (event) {
      var text = typeof event.data === 'string' ? event.data : '';
      if (typeof SendMessage === 'function') {
        try { SendMessage(target, 'OnWsMessage', text); } catch (e) { queue.push(text); }
      } else {
        queue.push(text);
      }
    };
    ws.onclose = function () {
      if (typeof SendMessage === 'function') {
        try { SendMessage(target, 'OnWsClose', ''); } catch (e) {}
      }
    };
  },
  TerritoryWsSend: function (textPtr) {
    var text = UTF8ToString(textPtr);
    var ws = typeof window !== 'undefined' ? window.__territoryWs : null;
    if (ws && ws.readyState === 1) ws.send(text);
  },
  TerritoryWsClose: function () {
    var ws = typeof window !== 'undefined' ? window.__territoryWs : null;
    if (ws) { try { ws.close(); } catch (e) {} }
    window.__territoryWs = null;
  },
  TerritoryWsPoll: function () {
    var queue = (typeof window !== 'undefined' && window.__territoryWsQueue) || [];
    var batch = queue.join('\n');
    queue.length = 0;
    var size = lengthBytesUTF8(batch) + 1;
    var ptr = _malloc(size);
    stringToUTF8(batch, ptr, size);
    return ptr;
  }
});
