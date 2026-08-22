#!/usr/bin/env python3
"""Evaluate whole-word correction accuracy and tune a safe threshold."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
import torch

from train_gap_model import (
    GapModel,
    WordExample,
    gap_context,
    load_examples,
)


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path, default=script_dir / "artifacts" / "broken_keys_ru.tsv.gz")
    parser.add_argument("--model", type=Path, default=script_dir / "artifacts" / "broken_key_gap_model.json")
    parser.add_argument("--output", type=Path, default=script_dir / "artifacts" / "word_metrics.json")
    parser.add_argument("--beam-size", type=int, default=16)
    parser.add_argument("--top-labels", type=int, default=2)
    parser.add_argument("--batch-size", type=int, default=16384)
    return parser.parse_args()


def load_model(path: Path) -> tuple[GapModel, dict]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    embedding = torch.tensor(payload["embedding"], dtype=torch.float32)
    hidden_weight = torch.tensor(payload["hidden_weight"], dtype=torch.float32)
    output_weight = torch.tensor(payload["output_weight"], dtype=torch.float32)
    model = GapModel(
        embedding.shape[0],
        output_weight.shape[0],
        payload["window"],
        embedding.shape[1],
        hidden_weight.shape[0],
    )
    with torch.no_grad():
        model.embedding.weight.copy_(embedding)
        model.hidden.weight.copy_(hidden_weight)
        model.hidden.bias.copy_(torch.tensor(payload["hidden_bias"], dtype=torch.float32))
        model.output.weight.copy_(output_weight)
        model.output.bias.copy_(torch.tensor(payload["output_bias"], dtype=torch.float32))
    model.eval()
    return model, payload


def infer_gap_log_probabilities(
    model: GapModel,
    examples: list[WordExample],
    character_ids: dict[str, int],
    window: int,
    batch_size: int,
) -> list[np.ndarray]:
    contexts: list[list[int]] = []
    ranges: list[tuple[int, int]] = []
    for example in examples:
        start = len(contexts)
        contexts.extend(
            gap_context(example.observed, gap, window, character_ids)
            for gap in range(len(example.observed) + 1)
        )
        ranges.append((start, len(contexts)))
    context_array = np.asarray(contexts, dtype=np.int64)
    outputs: list[np.ndarray] = []
    with torch.inference_mode():
        for start in range(0, len(context_array), batch_size):
            tensor = torch.from_numpy(context_array[start : start + batch_size])
            outputs.append(torch.log_softmax(model(tensor), dim=1).numpy())
    probabilities = np.concatenate(outputs, axis=0)
    return [probabilities[start:end] for start, end in ranges]


def candidate_beam(
    observed: str,
    gap_log_probabilities: np.ndarray,
    labels: list[str],
    vocabulary: set[str],
    frequencies: dict[str, int],
    beam_size: int,
    top_labels: int,
) -> list[tuple[str, float, float]]:
    # Each beam score is relative to selecting the empty label in every gap.
    beam: list[tuple[str, int, float]] = [("", 0, 0.0)]
    for gap, row in enumerate(gap_log_probabilities):
        empty_log_probability = float(row[0])
        nonempty_ids = np.argpartition(row[1:], -top_labels)[-top_labels:] + 1
        nonempty_ids = sorted(nonempty_ids, key=lambda index: float(row[index]), reverse=True)
        options = [("", 0, 0.0)]
        for label_id in nonempty_ids:
            insertion = labels[int(label_id)]
            # Very low probability alternatives only expand the beam and can
            # never survive a conservative whole-word threshold.
            gain = float(row[label_id]) - empty_log_probability
            if gain >= -8.0:
                options.append((insertion, len(insertion), gain))
        next_beam: list[tuple[str, int, float]] = []
        suffix = observed[gap] if gap < len(observed) else ""
        for prefix, inserted, score in beam:
            for insertion, insertion_length, gain in options:
                if inserted + insertion_length <= 3:
                    next_beam.append((prefix + insertion + suffix, inserted + insertion_length, score + gain))
        next_beam.sort(key=lambda item: item[2], reverse=True)
        beam = next_beam[:beam_size]

    candidates: list[tuple[str, float, float]] = []
    seen: set[str] = set()
    observed_frequency = frequencies.get(observed, 0)
    for word, inserted, model_gain in beam:
        if word in seen or word not in vocabulary:
            continue
        seen.add(word)
        frequency_gain = math.log1p(frequencies.get(word, 0)) - math.log1p(observed_frequency)
        candidates.append((word, model_gain, frequency_gain))
    if observed in vocabulary and observed not in seen:
        candidates.append((observed, 0.0, 0.0))
    return candidates


def choose_candidate(candidates: list[tuple[str, float, float]], frequency_weight: float) -> tuple[str, float]:
    if not candidates:
        return "", -math.inf
    ranked = sorted(
        ((word, model_gain + frequency_weight * frequency_gain) for word, model_gain, frequency_gain in candidates),
        key=lambda item: (item[1], -len(item[0]), item[0]),
        reverse=True,
    )
    return ranked[0]


def metrics_for(
    examples: list[WordExample],
    candidate_lists: list[list[tuple[str, float, float]]],
    vocabulary: set[str],
    frequency_weight: float,
    threshold: float,
) -> dict[str, float | int]:
    positives = negatives = positive_correct = negative_correct = false_positives = 0
    weighted_total = weighted_correct = 0.0
    for example, candidates in zip(examples, candidate_lists):
        word, score = choose_candidate(candidates, frequency_weight)
        # A valid dictionary word is never rewritten from word-only evidence.
        # Cases such as `то` versus a broken-key `это` require sentence context;
        # guessing here would recreate the destructive false corrections this
        # model is intended to eliminate.
        prediction = (
            word
            if example.observed not in vocabulary and word and word != example.observed and score >= threshold
            else example.observed
        )
        weight = max(1.0, math.log2(example.frequency + 2))
        weighted_total += weight
        weighted_correct += weight if prediction == example.expected else 0.0
        if example.correction:
            positives += 1
            positive_correct += prediction == example.expected
        else:
            negatives += 1
            negative_correct += prediction == example.observed
            false_positives += prediction != example.observed
    return {
        "rows": len(examples),
        "positives": positives,
        "negatives": negatives,
        "positive_exact_accuracy": positive_correct / max(1, positives),
        "negative_preservation_accuracy": negative_correct / max(1, negatives),
        "false_positive_rate": false_positives / max(1, negatives),
        "overall_accuracy": (positive_correct + negative_correct) / max(1, len(examples)),
        "log_frequency_weighted_accuracy": weighted_correct / max(1.0, weighted_total),
    }


def main() -> None:
    args = parse_args()
    model, payload = load_model(args.model)
    examples_by_split = load_examples(args.dataset)
    all_examples = examples_by_split["train"] + examples_by_split["validation"] + examples_by_split["test"]
    vocabulary = {example.expected for example in all_examples if example.expected == example.observed}
    frequencies: dict[str, int] = {}
    for example in all_examples:
        frequencies[example.expected] = max(frequencies.get(example.expected, 0), example.frequency)
    character_ids = {character: index for index, character in enumerate(payload["characters"])}

    candidate_lists: dict[str, list[list[tuple[str, float, float]]]] = {}
    for split in ("validation", "test"):
        examples = examples_by_split[split]
        print(f"inferring {split}: {len(examples)} words")
        gap_predictions = infer_gap_log_probabilities(
            model, examples, character_ids, payload["window"], args.batch_size
        )
        candidate_lists[split] = [
            candidate_beam(
                example.observed,
                prediction,
                payload["labels"],
                vocabulary,
                frequencies,
                args.beam_size,
                args.top_labels,
            )
            for example, prediction in zip(examples, gap_predictions)
        ]

    # Choose the most accurate validation configuration whose false-positive
    # rate does not exceed one correction per thousand valid words.
    configurations: list[tuple[float, float, dict]] = []
    for frequency_weight in (0.0, 0.10, 0.20, 0.35, 0.50):
        for threshold in (-6.0, -4.0, -3.0, -2.0, -1.0, 0.0, 0.5, 1.0, 2.0):
            metrics = metrics_for(
                examples_by_split["validation"],
                candidate_lists["validation"],
                vocabulary,
                frequency_weight,
                threshold,
            )
            if metrics["false_positive_rate"] <= 0.001:
                configurations.append((frequency_weight, threshold, metrics))
    if not configurations:
        raise SystemExit("no validation configuration met the false-positive limit")
    frequency_weight, threshold, validation_metrics = max(
        configurations,
        key=lambda item: (item[2]["positive_exact_accuracy"], item[2]["log_frequency_weighted_accuracy"]),
    )
    test_metrics = metrics_for(
        examples_by_split["test"], candidate_lists["test"], vocabulary, frequency_weight, threshold
    )
    result = {
        "schema_version": 1,
        "beam_size": args.beam_size,
        "top_labels_per_gap": args.top_labels,
        "frequency_weight": frequency_weight,
        "correction_score_threshold": threshold,
        "validation": validation_metrics,
        "test": test_metrics,
    }
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    payload["frequency_weight"] = frequency_weight
    payload["correction_score_threshold"] = threshold
    args.model.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
