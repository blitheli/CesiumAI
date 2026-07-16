/** 穷尽检查：未覆盖的联合成员在运行时抛错。 */
export function assertNever(value: never, message?: string): never {
  throw new Error(message ?? `未预期的值：${String(value)}`);
}
