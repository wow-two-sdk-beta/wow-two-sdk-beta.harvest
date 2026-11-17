import { buildMeasurements, computeRange, itemOffset } from '@wow-two-beta/fe/foundation/virtualization';
import { computed, shallowRef, watch, type Ref } from 'vue';

/** Chooses a bounded page from the number of viewports consumed per second. */
export function continuationSize(speed: number, viewportSize: number, minimum: number, maximum: number): number {
  const screensPerSecond = (speed * 1000) / Math.max(1, viewportSize);
  return Math.min(maximum, minimum * Math.max(1, Math.ceil(screensPerSecond / 1.5)));
}

/** Windows the loaded content within its page scroller and prefetches using velocity and measured request latency. */
export function useAdaptiveList(
  list: Ref<HTMLElement | null>,
  options: {
    readonly count: () => number;
    readonly grid: () => boolean;
    readonly enabled: () => boolean;
    readonly canLoad: () => boolean;
    readonly pageSizes: () => { minimum: number; maximum: number };
    readonly load: (limit: number) => Promise<boolean>;
  },
) {
  const layout = shallowRef({ width: 0, columns: 1, rowSize: 64 });
  const viewport = shallowRef({ offset: 0, height: 0 });
  const heights = new Map<number, number>();
  const measurementVersion = shallowRef(0);
  const rowCount = computed(() => Math.ceil(options.count() / layout.value.columns));
  const measured = computed(() => {
    void measurementVersion.value;
    return options.grid() ? undefined : buildMeasurements(rowCount.value, (index) => heights.get(index) ?? 64);
  });
  const range = computed(() => {
    const count = options.count();
    if (!options.enabled() || !viewport.value.height) return { start: 0, end: count, paddingStart: 0, paddingEnd: 0 };
    const { columns, rowSize } = layout.value;
    const rows = measured.value;
    const window = computeRange({
      scrollOffset: viewport.value.offset,
      viewportSize: viewport.value.height,
      itemCount: rowCount.value,
      ...(rows ? { getOffset: (index: number) => itemOffset(rows, index) } : { estimateSize: rowSize }),
      overscan: options.grid() ? 2 : 6,
    });
    return {
      start: window.startIndex * columns,
      end: Math.min(count, (window.endIndex + 1) * columns),
      paddingStart: window.paddingStart,
      paddingEnd: window.paddingEnd,
    };
  });

  let queue: (() => void) | undefined;
  watch(
    [list, options.grid],
    ([element], _previous, onCleanup) => {
      const window = element?.ownerDocument.defaultView;
      if (!element || !window) return;
      const document = element.ownerDocument;
      let scroller = element.parentElement;
      while (scroller && !/^(auto|scroll|overlay)$/.test(window.getComputedStyle(scroller).overflowY))
        scroller = scroller.parentElement;
      const scrollTarget = scroller ?? window;
      let frame = 0;
      let lastPosition = scroller?.scrollTop ?? window.scrollY;
      let lastMotion = window.performance.now();
      let speed = 0;
      let latency = 250;
      let loading = false;
      let attached = true;
      heights.clear();
      measurementVersion.value++;

      function read() {
        frame = 0;
        if (!element || !window) return;
        const rect = element.getBoundingClientRect();
        const style = window.getComputedStyle(element);
        const width = element.clientWidth;
        const columns = options.grid() ? Math.max(1, style.gridTemplateColumns.trim().split(/\s+/).length) : 1;
        const gap = Number.parseFloat(style.rowGap) || 0;
        const cardWidth = (width - (columns - 1) * gap) / columns;
        const rowSize = options.grid() ? Math.max(1, cardWidth * 1.25 + gap) : 64;
        if (width !== layout.value.width || columns !== layout.value.columns || rowSize !== layout.value.rowSize) {
          layout.value = { width, columns, rowSize };
          heights.clear();
          measurementVersion.value++;
        }

        if (!options.grid()) {
          let changed = false;
          for (const row of element.children) {
            const index = Number(row.getAttribute('data-post-index'));
            const size = row.getBoundingClientRect().height;
            if (size > 0 && heights.get(index) !== size) {
              heights.set(index, size);
              changed = true;
            }
          }
          if (changed) measurementVersion.value++;
        }

        const top = scroller ? scroller.getBoundingClientRect().top + scroller.clientTop : 0;
        const height = scroller?.clientHeight ?? document.documentElement.clientHeight;
        const bottom = top + height;
        const offset = Math.max(0, top - rect.top);
        const visibleHeight = Math.max(1, bottom - Math.max(top, rect.top));
        if (offset !== viewport.value.offset || visibleHeight !== viewport.value.height)
          viewport.value = { offset, height: visibleHeight };

        const now = window.performance.now();
        const position = scroller?.scrollTop ?? window.scrollY;
        const moved = position - lastPosition;
        if (moved !== 0) {
          const sample = Math.max(0, moved) / Math.max(16, now - lastMotion);
          speed = 0.35 * speed * Math.exp(-(now - lastMotion) / 300) + 0.65 * sample;
          lastMotion = now;
          lastPosition = position;
        }
        const velocity = speed * Math.exp(-(now - lastMotion) / 300);
        const lookahead = Math.min(4 * height, 0.75 * height + velocity * (latency + 150));
        if (
          options.enabled() &&
          options.canLoad() &&
          !loading &&
          height > 0 &&
          rect.top < bottom &&
          rect.bottom - bottom <= lookahead
        ) {
          const { minimum, maximum } = options.pageSizes();
          loading = true;
          void options.load(continuationSize(velocity, height, minimum, maximum)).then((loaded) => {
            loading = false;
            if (loaded && attached) {
              latency = 0.7 * latency + 0.3 * (window.performance.now() - now);
              schedule();
            }
          });
        }
      }

      function schedule() {
        if (!frame && window) frame = window.requestAnimationFrame(read);
      }

      queue = schedule;
      scrollTarget.addEventListener('scroll', schedule, { passive: true });
      const observer = new ResizeObserver(schedule);
      observer.observe(element);
      if (scroller) observer.observe(scroller);
      window.addEventListener('resize', schedule);
      schedule();
      onCleanup(() => {
        attached = false;
        queue = undefined;
        scrollTarget.removeEventListener('scroll', schedule);
        window.removeEventListener('resize', schedule);
        observer.disconnect();
        window.cancelAnimationFrame(frame);
      });
    },
    { flush: 'post' },
  );

  watch([options.count, options.enabled, options.canLoad, range], () => queue?.(), { flush: 'post' });
  return range;
}
