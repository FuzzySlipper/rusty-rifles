import {
  createLiveDebugHttpTransport,
  mountLiveDebugPanel,
  mountRendererMetricsWidget,
  type LiveDebugPanelMount,
} from '@rusty-engine/live-debug';

/** Engine owns commands, diagnostics and metric sampling; Rifles only hosts the UI. */
export function mountDebugTools(root: Element): Readonly<{ dispose(): void }> {
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Debug tools');
  panel.dataset.rustyUiInteractive = 'true';
  panel.style.cssText = 'box-sizing:border-box;position:fixed;right:12px;top:12px;z-index:2;max-width:calc(100vw - 24px);max-height:calc(100vh - 24px);overflow:auto;padding:8px;background:#171914f2;color:#eee6d5;border:1px solid #74694e;border-radius:5px;font:13px/1.35 system-ui;pointer-events:auto';
  // Let the console handle typing normally, without sending keys to gameplay.
  const isolate = (event: Event): void => event.stopPropagation();
  const events = ['pointerdown', 'pointermove', 'pointerup', 'pointercancel', 'mousedown', 'mousemove', 'mouseup', 'wheel', 'keydown', 'keyup', 'click'];
  for (const event of events) panel.addEventListener(event, isolate);

  const toolbar = document.createElement('div');
  toolbar.style.cssText = 'display:flex;flex-wrap:wrap;gap:5px;align-items:center';
  const title = document.createElement('strong'); title.textContent = 'Debug';
  const button = (label: string): HTMLButtonElement => {
    const element = document.createElement('button');
    element.type = 'button'; element.textContent = label;
    return element;
  };
  const consoleButton = button('console');
  consoleButton.setAttribute('aria-expanded', 'false');
  consoleButton.setAttribute('aria-controls', 'rifles-debug-console');
  const metricsButton = button('metrics');
  metricsButton.setAttribute('aria-pressed', 'false');
  toolbar.append(title, consoleButton, metricsButton);
  const status = document.createElement('p');
  status.setAttribute('role', 'status'); status.hidden = true;
  const metricsHost = document.createElement('div');
  metricsHost.setAttribute('aria-label', 'Renderer performance metrics');
  metricsHost.style.cssText = 'max-width:min(600px,calc(100vw - 44px));overflow:auto';
  const consoleHost = document.createElement('div');
  consoleHost.id = 'rifles-debug-console'; consoleHost.hidden = true;
  consoleHost.style.cssText = 'width:min(600px,calc(100vw - 44px));margin-top:8px';
  const style = document.createElement('style');
  style.textContent = '#rifles-debug-console [aria-label="Command completions"] { max-height: 8rem; overflow: auto; }';
  panel.append(style, toolbar, status, metricsHost, consoleHost);
  root.append(panel);

  const transport = createLiveDebugHttpTransport();
  // Preserve visibility selected through Engine console commands across UI remounts.
  const metrics = mountRendererMetricsWidget(metricsHost, { transport });
  const requests = new AbortController();
  let consolePanel: LiveDebugPanelMount | null = null;
  let disposed = false;
  const report = (error: unknown): void => {
    if (disposed) return;
    status.textContent = error instanceof Error ? error.message : String(error);
    status.hidden = false;
  };
  let metricsVisible = false;
  const setMetrics = async (visible: boolean): Promise<void> => {
    metricsButton.disabled = true;
    status.hidden = false; status.textContent = 'Waiting for Engine metrics command…';
    try {
      const result = await transport.execute(visible ? 'engine.renderer.show' : 'engine.renderer.hide', requests.signal);
      if (disposed) return;
      if (!result.succeeded) throw new Error(result.message);
      metricsVisible = visible;
      metricsButton.setAttribute('aria-pressed', String(visible));
      status.hidden = true;
    } catch (error) { report(error); }
    finally { if (!disposed) metricsButton.disabled = false; }
  };
  metricsButton.addEventListener('click', () => { void setMetrics(!metricsVisible); });
  consoleButton.addEventListener('click', () => {
    if (consolePanel !== null) {
      consolePanel.dispose(); consolePanel = null;
      consoleHost.replaceChildren(); consoleHost.hidden = true;
      consoleButton.textContent = 'console';
      consoleButton.setAttribute('aria-expanded', 'false');
      return;
    }
    consoleButton.disabled = true;
    consoleHost.hidden = false;
    status.hidden = false; status.textContent = 'Opening Engine debug console…';
    void mountLiveDebugPanel(consoleHost, { enabled: true, presentation: 'inline', transport }).then((mounted) => {
      if (disposed) { mounted.dispose(); return; }
      consolePanel = mounted;
      consoleButton.textContent = 'close';
      consoleButton.setAttribute('aria-expanded', 'true');
      status.hidden = true;
    }).catch((error: unknown) => {
      if (disposed) return;
      consoleHost.replaceChildren(); consoleHost.hidden = true;
      report(error);
    }).finally(() => { if (!disposed) consoleButton.disabled = false; });
  });

  return { dispose() {
    if (disposed) return;
    disposed = true;
    requests.abort(); consolePanel?.dispose(); metrics.dispose();
    for (const event of events) panel.removeEventListener(event, isolate);
    panel.remove();
  } };
}
