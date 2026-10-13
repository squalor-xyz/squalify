Prompt: a file profiler and a config proposer for DataBall

Build two deterministic tools that help a person write a DataBall file type for a new lab file format. Given the same input files and the same tool version, both must always produce the same output, byte for byte. Neither uses machine learning, an LLM, or the network.

Background (current DataBall):
- DataBall.Detect(path) and databall detect --explain (db-10) score handlers and built-in routes. They report which one opens a file and why.
- FileProbe reads a bounded head of at most 64 KiB: delimiter, header fields, and Parquet column names.
- A JSON file type declares match rules and a config. Folder types load from ~/.databall/types and ./databall/types.
- A Config declares the schema (columns, types, metadata fields and policies, and optional tables layouts). The schema always comes from config, not from sniffing at open time.
