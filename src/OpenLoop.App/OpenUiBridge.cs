using System.Text;

namespace OpenLoop.App
{
    /// <summary>
    /// 注入到每个新文档的桥垫片：把 <c>postMessage</c> 往返包装成 <c>ui.call</c> 的 Promise。
    /// 只有映射域里的页面才会真正拿到它（壳按来源校验来源，垫片自己也先判 origin）。
    /// 页面被导航到远程站点后垫片仍在，但两端都不响应，等于没有。
    /// </summary>
    internal static class OpenUiBridge
    {
        /// <summary>与 openui.js 里的 <c>ui.call</c> 同线上形状；openui.js 优先用自己那份，垫片只做兜底。</summary>
        public const string Shim = """
(function () {
  if (window.__openuiShim) return;
  window.__openuiShim = true;
  var ORIGIN = 'https://openui.local';
  if (location.origin !== ORIGIN) return;

  var wv = window.chrome && chrome.webview;
  if (!wv || typeof wv.postMessage !== 'function') return;

  var seq = 0;
  var pending = new Map();

  wv.addEventListener('message', function (e) {
    var msg = e.data;
    if (!msg || !msg.id || !pending.has(msg.id)) return;
    var p = pending.get(msg.id);
    pending.delete(msg.id);
    if (msg.ok) p.resolve(msg.data);
    else p.reject(new Error(msg.error || '调用失败'));
  });

  function call(action, args) {
    return new Promise(function (resolve, reject) {
      var id = 's' + (++seq);
      pending.set(id, { resolve: resolve, reject: reject });
      try { wv.postMessage({ id: id, action: action, args: args || {} }); }
      catch (err) { pending.delete(id); reject(err); return; }
      setTimeout(function () {
        if (pending.has(id)) { pending.delete(id); reject(new Error('调用超时：' + action)); }
      }, 20000);
    });
  }

  // openui.js 如果还没加载（或加载失败），至少留一个能提交数据的入口
  window.ui = window.ui || {};
  if (!window.ui.call) window.ui.call = call;
})();
""";

        /// <summary>把页面发来的 JSON 消息交给视图处理（来源由视图再校验一次）。</summary>
        public static void Dispatch(BrowserView view, string source, string json)
            => view?.OnWebMessage(source, json);

        /// <summary>页面里执行 <c>ui.call</c> 需要的 action 名单（供技能文档引用）。</summary>
        public static string ActionList => string.Join(", ",
            "SessionState", "SessionSubmit", "SessionStop",
            "LoopAppOpen", "LoopAppEval", "LoopAppShot", "LoopAppState",
            "LoopAppUndock", "LoopAppDock", "LoopAppInteract");
    }
}
