import {
  MAX_SEMANTIC_JSON_BYTES,
  NUMBER_BUDGET_BYTES,
  measureSemanticJsonSize,
} from "./semanticJsonSize";

it("matches literal object/array/primitive budgets", () => {
  expect(measureSemanticJsonSize({})).toBe(2);

  // {"label":null} => braces 2 + "label" 7 + colon 1 + null 4 = 14
  expect(measureSemanticJsonSize({ label: null })).toBe(14);

  // {"path":{"width":5}} => 2+6+1+ (2+7+1+24) = 43
  expect(measureSemanticJsonSize({ path: { width: 5 } })).toBe(43);

  // [true,false,null] => 2+4+1+5+1+4 = 17
  expect(measureSemanticJsonSize([true, false, null])).toBe(17);

  // {"label":{"text":"a\"b"}} ；"a\"b" JSON UTF-8 长度为 6
  expect(
    measureSemanticJsonSize({ label: { text: 'a"b' } }),
  ).toBe(2 + 7 + 1 + (2 + 6 + 1 + 6));
});

it("counts every finite number as a fixed 24-byte worst-case budget", () => {
  expect(measureSemanticJsonSize(1)).toBe(NUMBER_BUDGET_BYTES);
  expect(measureSemanticJsonSize(1e20)).toBe(NUMBER_BUDGET_BYTES);
  expect(NUMBER_BUDGET_BYTES).toBe(24);
  expect(MAX_SEMANTIC_JSON_BYTES).toBe(32 * 1024);
});

it.each([
  ["你好", 8],
  ["<", 3],
  ['a"b\\c', 9],
  ["\u0000\u0001\n\t", 18],
  ["\u2028", 5],
  ["\u2029", 5],
  ["a<\u2028\u2029>你好", 17],
] as const)(
  "string %j matches browser JSON.stringify UTF-8 byte count %i",
  (value, expectedBytes) => {
    expect(measureSemanticJsonSize(value)).toBe(expectedBytes);
  },
);

it("treats raw and unicode-escape forms as the same semantic string", () => {
  expect(measureSemanticJsonSize(JSON.parse('"你好"'))).toBe(8);
  expect(measureSemanticJsonSize(JSON.parse('"\\u4f60\\u597d"'))).toBe(8);
  expect(measureSemanticJsonSize(JSON.parse('"\\u2028"'))).toBe(5);
  expect(measureSemanticJsonSize("\u2028")).toBe(5);
});

it("does not confuse literal backslash-u sequence with line separator", () => {
  // 语义为 \\u2028 六个可见字符 → JSON.stringify => "\\u2028"（9 bytes）
  expect(measureSemanticJsonSize("\\u2028")).toBe(9);
});

it("encodes object keys with the same browser-aligned string rules", () => {
  expect(measureSemanticJsonSize({ "a<\u2028": null })).toBe(14);
});
