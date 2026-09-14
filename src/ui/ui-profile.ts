interface SlowFrame extends PerformanceEntry {
  renderStart: number;
  styleAndLayoutStart: number;
  scripts: Array<{
    duration: number;
    invoker: string;
    sourceURL: string;
    sourceFunctionName: string;
    sourceCharPosition: number;
    forcedStyleAndLayoutDuration: number;
  }>;
}

/** Bounded timings of this browser's product DOM callback, not Engine rendering. */
export class UiProfile {
  private readonly samples: Array<{ duration: number; gap: number }> = [];
  private previous = 0;
  private readonly longTasks: number[] = [];
  private readonly observer: PerformanceObserver | null;
  private readonly frames: SlowFrame[] = [];
  private readonly frameObserver: PerformanceObserver | null;
  constructor() {
    this.observer = typeof PerformanceObserver !== 'undefined' && PerformanceObserver.supportedEntryTypes.includes('longtask')
      ? new PerformanceObserver(list => {
        for (const entry of list.getEntries()) {
          this.longTasks.push(entry.duration);
          if (this.longTasks.length > 256) this.longTasks.shift();
        }
      }) : null;
    this.observer?.observe({ type: 'longtask', buffered: true });
    this.frameObserver = typeof PerformanceObserver !== 'undefined' && PerformanceObserver.supportedEntryTypes.includes('long-animation-frame')
      ? new PerformanceObserver(list => {
        for (const entry of list.getEntries()) this.frames.push(entry as SlowFrame);
        this.frames.sort((a, b) => b.duration - a.duration);
        this.frames.splice(5);
      }) : null;
    this.frameObserver?.observe({ type: 'long-animation-frame', buffered: true });
  }
  dispose(): void { this.observer?.disconnect(); this.frameObserver?.disconnect(); }
  measure(action: () => void): void {
    const start = performance.now();
    const gap = this.previous === 0 ? 0 : start - this.previous;
    this.previous = start;
    try { action(); }
    finally {
      this.samples.push({ duration: performance.now() - start, gap });
      if (this.samples.length > 256) this.samples.shift();
    }
  }
  read(): string {
    const summary = (field: 'duration' | 'gap'): string => {
      const values = this.samples.map(sample => sample[field]).sort((a, b) => a - b);
      if (values.length === 0) return 'no samples';
      const percentile = (fraction: number): number => values[Math.max(0, Math.ceil(values.length * fraction) - 1)]!;
      return `p50 ${percentile(0.5).toFixed(2)} ms · p95 ${percentile(0.95).toFixed(2)} ms · max ${percentile(1).toFixed(2)} ms`;
    };
    const frames = this.frameObserver === null ? 'Slow-frame attribution unavailable' : this.frames.map(frame => {
      const layout = frame.styleAndLayoutStart > 0 ? frame.startTime + frame.duration - frame.styleAndLayoutStart : 0;
      const scripts = frame.scripts.map(script => `  ${script.duration.toFixed(2)} ms · ${script.invoker} · ${script.sourceURL}:${script.sourceCharPosition} ${script.sourceFunctionName} · forced layout ${script.forcedStyleAndLayoutDuration.toFixed(2)} ms`).join('\n');
      return `Slow frame ${frame.duration.toFixed(2)} ms · layout/paint tail ${layout.toFixed(2)} ms · age ${(performance.now() - frame.startTime).toFixed(0)} ms\n${scripts}`;
    }).join('\n');
    return `Rifles UI callback in this browser (${this.samples.length} retained)\nDOM callback: ${summary('duration')}\nDelivery gap: ${summary('gap')}\nAge: ${this.previous === 0 ? 'unavailable' : (performance.now() - this.previous).toFixed(0) + ' ms'}\nBrowser long tasks: ${this.observer === null ? 'unavailable' : this.longTasks.length + ' retained; longest ' + Math.max(0, ...this.longTasks).toFixed(2) + ' ms'}\nExcludes Engine decoding, layout/paint after callback, and GPU work.\n${frames}`;
  }
}
