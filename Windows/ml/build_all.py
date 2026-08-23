#!/usr/bin/env python3
"""One deterministic entry point for the Windows Russian broken-key V2 pipeline."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--pymorphy-dictionary", type=Path)
    source.add_argument("--opencorpora-dictionary", type=Path)
    parser.add_argument("--opencorpora-corpus", type=Path)
    parser.add_argument("--skip-existing", action="store_true")
    return parser.parse_args()


def run(script_dir: Path, script: str, arguments: list[str], output: Path | None = None, skip=False) -> None:
    if skip and output is not None and output.exists():
        print(f"skip {script}: {output} exists")
        return
    command = [sys.executable, str(script_dir / script), *arguments]
    print("run:", " ".join(command))
    subprocess.run(command, check=True, env={**os.environ, "PYTHONUTF8": "1"})


def main() -> None:
    args = parse_args()
    script_dir = Path(__file__).resolve().parent
    artifacts = script_dir / "artifacts" / "v2"
    source_argument = (
        ["--pymorphy-dictionary", str(args.pymorphy_dictionary)]
        if args.pymorphy_dictionary
        else ["--opencorpora-dictionary", str(args.opencorpora_dictionary)]
    )
    for path in [args.pymorphy_dictionary, args.opencorpora_dictionary, args.opencorpora_corpus]:
        if path is not None and not path.exists():
            raise SystemExit(f"missing input: {path}")

    run(script_dir, "build_lexicon.py", source_argument,
        artifacts / "ru_lexicon.tsv.gz", args.skip_existing)
    if args.opencorpora_corpus:
        run(script_dir, "build_language_stats.py",
            ["--opencorpora-corpus", str(args.opencorpora_corpus)],
            artifacts / "ru_ngrams.tsv.gz", args.skip_existing)
    elif not (artifacts / "ru_ngrams.tsv.gz").exists():
        print("degraded mode: no OpenCorpora corpus; n-gram context layer is omitted")

    run(script_dir, "export_runtime_resources.py", [],
        script_dir.parent / "src" / "LayoutGuard.Windows" / "Resources" / "Models" / "ru_broken_lexicon.bin",
        args.skip_existing)
    run(script_dir, "build_broken_dataset.py", [],
        artifacts / "broken_keys_v2.tsv.gz", args.skip_existing)
    run(script_dir, "evaluate_broken_keys.py", [],
        artifacts / "evaluation_metrics.json", args.skip_existing)

    manifests = {}
    for path in sorted(artifacts.glob("*manifest.json")):
        manifests[path.name] = json.loads(path.read_text(encoding="utf-8"))
    print(json.dumps({"pipeline": "complete", "manifests": manifests}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
