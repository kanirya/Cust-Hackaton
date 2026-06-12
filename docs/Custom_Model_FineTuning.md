# TaxNet Guardian — Fine-tuning Your Own Local Model

This guide turns the **teacher data** captured from the frontier LLM (Claude) into a **real
fine-tuned local model** that TaxNet Guardian can serve through the same inference switch.

The platform already does the hard parts:
- Captures every frontier-LLM `(prompt → response)` pair as a teacher example.
- Exports them as fine-tuning-ready JSONL.
- Routes inference to a local model via the **Fine-tuned (Ollama)** button — no code change needed.

You bring the one step a C# web app can't do in-process: the actual GPU fine-tune.

---

## 1. Collect teacher data

Use the platform normally with **Inference Routing = Frontier LLM**. Every CNIC investigation,
assistant chat, deep-explain, and report draft is captured. Aim for a few hundred examples across
all task types (CnicInvestigation, AuditExplanation, CitizenExplanation, ReportDraft, PolicyQuestion).

Check progress on the **Model Training** page → *Teacher examples*.

## 2. Export the dataset

On the Model Training page click **Export JSONL (chat)** (or call the API):

```
GET /api/system/custom-model/export?format=chat        # OpenAI messages[] format (recommended)
GET /api/system/custom-model/export?format=instruction # {instruction,input,output} format
```

Each line is one training sample, e.g. (chat format):

```json
{"messages":[{"role":"system","content":"You are TaxNet Guardian..."},{"role":"user","content":"<prompt>"},{"role":"assistant","content":"<frontier answer>"}]}
```

## 3. Fine-tune a small base model (GPU step, done offline)

Pick a base model that fits your GPU. Good starting points:

| Base model | VRAM (LoRA 4-bit) | Notes |
|---|---|---|
| Llama 3.1 8B Instruct | ~8–12 GB | Best general quality/size tradeoff |
| Qwen2.5 7B Instruct | ~8–12 GB | Strong reasoning, multilingual (good for Urdu) |
| Phi-3.5 mini (3.8B) | ~4–6 GB | Runs on modest GPUs |

### Option A — Unsloth (simplest, free Colab)

```python
from unsloth import FastLanguageModel
from datasets import load_dataset
from trl import SFTTrainer
from transformers import TrainingArguments

model, tokenizer = FastLanguageModel.from_pretrained(
    "unsloth/Meta-Llama-3.1-8B-Instruct-bnb-4bit", max_seq_length=4096, load_in_4bit=True)
model = FastLanguageModel.get_peft_model(model, r=16, lora_alpha=16,
    target_modules=["q_proj","k_proj","v_proj","o_proj","gate_proj","up_proj","down_proj"])

ds = load_dataset("json", data_files="taxnet-training-chat.jsonl", split="train")
def fmt(b): return {"text": tokenizer.apply_chat_template(b["messages"], tokenize=False)}
ds = ds.map(fmt)

SFTTrainer(model=model, tokenizer=tokenizer, train_dataset=ds, dataset_text_field="text",
    args=TrainingArguments(per_device_train_batch_size=2, gradient_accumulation_steps=4,
        warmup_steps=5, num_train_epochs=2, learning_rate=2e-4, fp16=True, logging_steps=5,
        output_dir="taxnet-lora")).train()

model.save_pretrained_gguf("taxnet-guardian-gguf", tokenizer, quantization_method="q4_k_m")
```

### Option B — Axolotl / OpenAI fine-tuning API
Both accept the same chat JSONL directly. For OpenAI, upload the file and fine-tune `gpt-4o-mini`,
then point `OPENAI_MODEL` at the fine-tuned model id (no Ollama needed).

## 4. Register the model in Ollama

```
# Create a Modelfile (see infra/ollama/Modelfile)
ollama create taxnet-guardian -f infra/ollama/Modelfile
ollama run taxnet-guardian "Explain why a non-filer with luxury assets is high risk."
```

## 5. Point TaxNet Guardian at it

Set these before starting the API:

```
OLLAMA_ENABLED=true
OLLAMA_BASE_URL=http://localhost:11434/v1
OLLAMA_MODEL=taxnet-guardian
```

Then on the **Model Training** page click **Fine-tuned (Ollama)**. All AI flows
(CNIC investigation, assistant, deep-explain, reports) now stream from your own model. Switch back
to **Frontier LLM** any time to keep collecting fresh teacher data and re-fine-tune periodically —
that's the continual-learning loop.

---

## Will it match the frontier model?

On your **narrow tax-domain tasks**, a well-fine-tuned 8B model can get close, because it specializes
in exactly your prompt/response style. It will **not** match a frontier model on open-ended general
reasoning — that ceiling is set by the base model's size, not by how much TaxNet data you add. To
raise the ceiling, fine-tune a larger base (e.g. 70B) on more GPU. More data → better specialization
and consistency; bigger base → higher raw capability.
