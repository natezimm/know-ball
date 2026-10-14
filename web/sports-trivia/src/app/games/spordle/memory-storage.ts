/**
 * Test helper. The test DOM on recent Node versions leaves `localStorage` undefined,
 * so specs that exercise saved stats or settings install this in-memory stand-in first.
 */
export function useMemoryStorage(): void {
  const data = new Map<string, string>();
  const storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem' | 'clear'> = {
    getItem: (key) => data.get(key) ?? null,
    setItem: (key, value) => void data.set(key, String(value)),
    removeItem: (key) => void data.delete(key),
    clear: () => data.clear(),
  };
  Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true, writable: true });
}
