# Broken-key model data (Windows)

This directory contains the reproducible data and evaluation pipeline for
restoring the selected Russian dead keys `п`, `р`, and `э`. It is independent
from the macOS application.

The generator reads the Russian Hunspell dictionary and the Russian frequency
list already shipped with the Windows application. It generates every unique
variant with one to three selected dead-key presses omitted and uses the
original word as the supervised target. Unchanged dictionary words are included
as negative “leave unchanged” examples.

The generator also writes a 2 MiB Bloom filter containing the exact training
vocabulary. At runtime this prevents the wider Hunspell affix engine from
accepting a generated form that the model never saw as a valid target.

Exact words are assigned to train/validation/test by a stable SHA-256 hash in an
80/10/10 ratio. This prevents the same word from leaking across splits. The
generated compressed TSV is intentionally not shipped in the installer.

```powershell
python Windows/ml/build_dataset.py
python Windows/ml/train_gap_model.py
python Windows/ml/evaluate_gap_model.py
python Windows/ml/predict_gap_model.py ривет вет потести релизь
```

The trained model is a small fixed-context neural classifier. For every gap in
an observed word it predicts either no insertion or a one-to-three-character
string composed only of `п`, `р`, and `э`. The Windows runtime can evaluate the
exported matrices directly; no Python or ML framework is required by users.

The source licenses are documented in `Windows/THIRD_PARTY.md` and copied into
the Windows application resources.
