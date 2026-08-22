#!/usr/bin/env python3
"""Train a tiny character-gap model for Russian broken-key restoration.

The network classifies what (if anything) should be inserted into every gap of
an observed word. It only sees a short character context, so it is small enough
to run synchronously in well under a millisecond without a Python or ONNX
runtime. The exported JSON contains plain matrices for the C# implementation.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import itertools
import json
import math
import os
import random
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

os.environ.setdefault("CUBLAS_WORKSPACE_CONFIG", ":4096:8")
import torch
from torch import nn


RUSSIAN_ALPHABET = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя"
PAD = "<pad>"
BOS = "<bos>"
EOS = "<eos>"


@dataclass(frozen=True)
class WordExample:
    observed: str
    expected: str
    correction: bool
    frequency: int


class GapModel(nn.Module):
    def __init__(self, vocabulary_size: int, label_count: int, window: int, embedding_size: int, hidden_size: int):
        super().__init__()
        self.embedding = nn.Embedding(vocabulary_size, embedding_size)
        self.hidden = nn.Linear(window * 2 * embedding_size, hidden_size)
        self.output = nn.Linear(hidden_size, label_count)

    def forward(self, contexts: torch.Tensor) -> torch.Tensor:
        embedded = self.embedding(contexts).flatten(start_dim=1)
        return self.output(torch.relu(self.hidden(embedded)))


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path, default=script_dir / "artifacts" / "broken_keys_ru.tsv.gz")
    parser.add_argument("--output", type=Path, default=script_dir / "artifacts" / "broken_key_gap_model.json")
    parser.add_argument("--metrics", type=Path, default=script_dir / "artifacts" / "model_metrics.json")
    parser.add_argument("--letters", default="прэ")
    parser.add_argument("--window", type=int, default=4)
    parser.add_argument("--embedding-size", type=int, default=12)
    parser.add_argument("--hidden-size", type=int, default=64)
    parser.add_argument("--epochs", type=int, default=4)
    parser.add_argument("--batch-size", type=int, default=4096)
    parser.add_argument("--learning-rate", type=float, default=0.002)
    parser.add_argument("--seed", type=int, default=20260822)
    return parser.parse_args()


def labels_for(letters: str, maximum_missing: int = 3) -> list[str]:
    labels = [""]
    for length in range(1, maximum_missing + 1):
        labels.extend("".join(chars) for chars in itertools.product(letters, repeat=length))
    return labels


def load_examples(path: Path) -> dict[str, list[WordExample]]:
    result = {"train": [], "validation": [], "test": []}
    with gzip.open(path, "rt", encoding="utf-8", newline="") as source:
        for row in csv.DictReader(source, delimiter="\t"):
            result[row["split"]].append(
                WordExample(
                    row["observed"],
                    row["expected"],
                    row["is_correction"] == "1",
                    int(row["frequency"]),
                )
            )
    return result


def align_insertions(observed: str, expected: str, letters: frozenset[str]) -> list[str] | None:
    gaps = [""] * (len(observed) + 1)
    observed_index = 0
    for character in expected:
        if observed_index < len(observed) and character == observed[observed_index]:
            observed_index += 1
        elif character in letters:
            gaps[observed_index] += character
        else:
            return None
    return gaps if observed_index == len(observed) else None


def gap_context(word: str, gap: int, window: int, character_ids: dict[str, int]) -> list[int]:
    left = ([BOS] * window + list(word[:gap]))[-window:]
    right = (list(word[gap:]) + [EOS] * window)[:window]
    return [character_ids.get(character, character_ids[PAD]) for character in left + right]


def build_gap_samples(
    examples: list[WordExample],
    letters: frozenset[str],
    label_ids: dict[str, int],
    character_ids: dict[str, int],
    window: int,
    seed: int,
) -> tuple[np.ndarray, np.ndarray]:
    contexts: list[list[int]] = []
    targets: list[int] = []
    for example in examples:
        insertions = align_insertions(example.observed, example.expected, letters)
        if insertions is None:
            continue
        nonempty = [index for index, insertion in enumerate(insertions) if insertion]
        empty = [index for index, insertion in enumerate(insertions) if not insertion]
        # Keep all informative insertion gaps and a deterministic small sample
        # of empty gaps. This avoids drowning the model in the empty class.
        sample_key = f"{seed}\0{example.observed}\0{example.expected}".encode("utf-8")
        sample_seed = int.from_bytes(hashlib.sha256(sample_key).digest()[:4], "big")
        rng = random.Random(sample_seed)
        rng.shuffle(empty)
        selected = nonempty + empty[: (3 if nonempty else 2)]
        for gap in selected:
            insertion = insertions[gap]
            if insertion not in label_ids:
                continue
            contexts.append(gap_context(example.observed, gap, window, character_ids))
            targets.append(label_ids[insertion])
    return np.asarray(contexts, dtype=np.int64), np.asarray(targets, dtype=np.int64)


def train_model(
    model: GapModel,
    contexts: np.ndarray,
    targets: np.ndarray,
    epochs: int,
    batch_size: int,
    learning_rate: float,
    device: torch.device,
    seed: int,
) -> list[float]:
    optimizer = torch.optim.AdamW(model.parameters(), lr=learning_rate, weight_decay=1e-5)
    criterion = nn.CrossEntropyLoss()
    generator = np.random.default_rng(seed)
    history: list[float] = []
    model.to(device)
    for epoch in range(epochs):
        order = generator.permutation(len(targets))
        total_loss = 0.0
        seen = 0
        model.train()
        started = time.perf_counter()
        for start in range(0, len(order), batch_size):
            indexes = order[start : start + batch_size]
            batch_contexts = torch.from_numpy(contexts[indexes]).to(device)
            batch_targets = torch.from_numpy(targets[indexes]).to(device)
            optimizer.zero_grad(set_to_none=True)
            logits = model(batch_contexts)
            loss = criterion(logits, batch_targets)
            loss.backward()
            optimizer.step()
            total_loss += float(loss) * len(indexes)
            seen += len(indexes)
        average = total_loss / max(1, seen)
        history.append(average)
        print(f"epoch={epoch + 1} loss={average:.6f} seconds={time.perf_counter() - started:.1f}")
    return history


def gap_accuracy(model: GapModel, contexts: np.ndarray, targets: np.ndarray, batch_size: int, device: torch.device) -> float:
    correct = 0
    model.eval()
    with torch.inference_mode():
        for start in range(0, len(targets), batch_size):
            batch = torch.from_numpy(contexts[start : start + batch_size]).to(device)
            predicted = model(batch).argmax(dim=1).cpu().numpy()
            correct += int((predicted == targets[start : start + batch_size]).sum())
    return correct / max(1, len(targets))


def export_model(
    model: GapModel,
    output: Path,
    letters: str,
    characters: list[str],
    labels: list[str],
    window: int,
    threshold: float,
) -> None:
    state = model.cpu().state_dict()
    payload = {
        "schema_version": 1,
        "letters": letters,
        "maximum_missing": 3,
        "window": window,
        "characters": characters,
        "labels": labels,
        "correction_probability_threshold": threshold,
        "embedding": state["embedding.weight"].tolist(),
        "hidden_weight": state["hidden.weight"].tolist(),
        "hidden_bias": state["hidden.bias"].tolist(),
        "output_weight": state["output.weight"].tolist(),
        "output_bias": state["output.bias"].tolist(),
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")


def main() -> None:
    args = parse_args()
    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)
    torch.use_deterministic_algorithms(True)
    characters = [PAD, BOS, EOS, *RUSSIAN_ALPHABET]
    character_ids = {character: index for index, character in enumerate(characters)}
    labels = labels_for(args.letters)
    label_ids = {label: index for index, label in enumerate(labels)}
    letters = frozenset(args.letters)
    examples = load_examples(args.dataset)

    print({split: len(rows) for split, rows in examples.items()})
    train_contexts, train_targets = build_gap_samples(
        examples["train"], letters, label_ids, character_ids, args.window, args.seed
    )
    validation_contexts, validation_targets = build_gap_samples(
        examples["validation"], letters, label_ids, character_ids, args.window, args.seed
    )
    test_contexts, test_targets = build_gap_samples(
        examples["test"], letters, label_ids, character_ids, args.window, args.seed
    )
    print(
        {
            "train_gap_samples": len(train_targets),
            "validation_gap_samples": len(validation_targets),
            "test_gap_samples": len(test_targets),
        }
    )

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model = GapModel(
        len(characters), len(labels), args.window, args.embedding_size, args.hidden_size
    )
    history = train_model(
        model,
        train_contexts,
        train_targets,
        args.epochs,
        args.batch_size,
        args.learning_rate,
        device,
        args.seed,
    )
    validation_accuracy = gap_accuracy(model, validation_contexts, validation_targets, args.batch_size, device)
    test_accuracy = gap_accuracy(model, test_contexts, test_targets, args.batch_size, device)

    # Probability calibration is deliberately conservative. Word-level beam
    # search can lower this only after its false-positive rate is measured.
    threshold = 0.90
    export_model(model, args.output, args.letters, characters, labels, args.window, threshold)
    metrics = {
        "schema_version": 1,
        "device": str(device),
        "parameters": sum(parameter.numel() for parameter in model.parameters()),
        "train_examples": len(examples["train"]),
        "validation_examples": len(examples["validation"]),
        "test_examples": len(examples["test"]),
        "train_gap_samples": len(train_targets),
        "validation_gap_samples": len(validation_targets),
        "test_gap_samples": len(test_targets),
        "loss": history,
        "validation_gap_accuracy": validation_accuracy,
        "test_gap_accuracy": test_accuracy,
        "model_bytes": args.output.stat().st_size,
    }
    args.metrics.write_text(json.dumps(metrics, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metrics, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
