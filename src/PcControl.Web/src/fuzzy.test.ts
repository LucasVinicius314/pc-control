import { describe, expect, it } from "vitest";
import { normalize, rank, scoreMatch } from "./fuzzy";

const names = (query: string, items: string[]) => rank(query, items, (s) => s).map((r) => r.item);

describe("normalize", () => {
  it("ignores case and accents", () => {
    expect(normalize("Édge")).toBe("edge");
    expect(normalize("Ação")).toBe("acao");
    expect(normalize("CAFÉ Naïve")).toBe("cafe naive");
  });
});

describe("scoreMatch", () => {
  it("rates an exact name above a prefix, a prefix above a word start, and so on down", () => {
    const exact = scoreMatch("code", "code")!;
    const prefix = scoreMatch("code", "code editor")!;
    const wordStart = scoreMatch("code", "visual studio code")!;
    const substring = scoreMatch("code", "barcode scanner")!;
    const subsequence = scoreMatch("cde", "visual studio code")!;

    expect(exact).toBeGreaterThan(prefix);
    expect(prefix).toBeGreaterThan(wordStart);
    expect(wordStart).toBeGreaterThan(substring);
    expect(substring).toBeGreaterThan(subsequence);
  });

  it("finds a word in the middle of a name", () => {
    expect(scoreMatch("studio", "Visual Studio Code")).not.toBeNull();
  });

  it("treats separators as word boundaries", () => {
    const hyphen = scoreMatch("zip", "7-zip file manager")!;
    const middle = scoreMatch("zip", "unzipper")!;

    expect(hyphen).toBeGreaterThan(middle);
  });

  it("needs every word of the query", () => {
    expect(scoreMatch("visual code", "Visual Studio Code")).not.toBeNull();
    expect(scoreMatch("visual banana", "Visual Studio Code")).toBeNull();
  });

  it("matches words in any order", () => {
    expect(scoreMatch("code visual", "Visual Studio Code")).not.toBeNull();
  });

  it("finds initials-style queries", () => {
    expect(scoreMatch("vscode", "Visual Studio Code")).not.toBeNull();
    expect(scoreMatch("vsc", "Visual Studio Code")).not.toBeNull();
  });

  it("finds queries made of the first letters of the words", () => {
    expect(scoreMatch("gc", "Google Chrome")).not.toBeNull();
    expect(scoreMatch("vsc", "Visual Studio Code")).not.toBeNull();
    expect(scoreMatch("wt", "Windows Terminal")).not.toBeNull();
  });

  it("needs the initials in order", () => {
    expect(scoreMatch("cg", "Google Chrome")).toBeNull();
  });

  it("rates initials below a word that really starts with the letters", () => {
    expect(scoreMatch("co", "Code Blocks")!).toBeGreaterThan(scoreMatch("co", "Calculator Online")!);
  });

  it("does not match letters scattered far apart", () => {
    expect(scoreMatch("chr", "Cyberpunk 2077 Phantom Liberty Hero Rpg Edition")).toBeNull();
  });

  it("does not use one-letter subsequences", () => {
    expect(scoreMatch("z", "Calculator")).toBeNull();
  });

  it("ignores accents and case in both the query and the name", () => {
    expect(scoreMatch("cafe", "Café")).not.toBeNull();
    expect(scoreMatch("CAFÉ", "cafe bar")).not.toBeNull();
    expect(scoreMatch("acao", "Ação")).not.toBeNull();
  });

  it("matches everything, equally, when the query is empty", () => {
    expect(scoreMatch("", "Anything")).toBe(0);
    expect(scoreMatch("   ", "Anything")).toBe(0);
  });

  it("returns null when nothing matches", () => {
    expect(scoreMatch("qqqq", "Notepad")).toBeNull();
  });

  it("prefers the shorter of two equally good names", () => {
    expect(scoreMatch("note", "Notepad")!).toBeGreaterThan(scoreMatch("note", "Notepad++ Portable Edition")!);
  });
});

describe("rank", () => {
  const apps = ["Notepad", "Calculator", "Visual Studio Code", "Code Blocks", "Paint", "Edge", "Terminal"];

  it("keeps the original order for an empty query", () => {
    expect(names("", apps)).toEqual(apps);
  });

  it("puts the best match first", () => {
    expect(names("code", apps)).toEqual(["Code Blocks", "Visual Studio Code"]);
  });

  it("drops what does not match", () => {
    expect(names("note", apps)).toEqual(["Notepad"]);
    expect(names("zzz", apps)).toEqual([]);
  });

  it("finds an app typed with the wrong accents", () => {
    expect(names("EDGE", ["Édge", "Notepad"])).toEqual(["Édge"]);
  });

  it("breaks ties alphabetically, so the order is stable", () => {
    expect(names("a", ["Banana", "Apple", "Avocado"]).slice(0, 2)).toEqual(["Apple", "Avocado"]);
  });

  it("returns the original items, with their scores", () => {
    const items = [{ id: 1, name: "Notepad" }];

    const [only] = rank("note", items, (i) => i.name);

    expect(only?.item).toBe(items[0]);
    expect(only?.score).toBeGreaterThan(0);
  });

  it("copes with a long real-world list quickly", () => {
    const many = Array.from({ length: 5000 }, (_, i) => `App number ${i} Pro Edition`);
    const started = performance.now();

    rank("pro ed 42", many, (s) => s);

    expect(performance.now() - started).toBeLessThan(500);
  });
});
