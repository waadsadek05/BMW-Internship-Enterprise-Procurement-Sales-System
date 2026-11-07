"""
llm_client.py
-------------
Thin wrapper around Groq's OpenAI-compatible chat-completions API.

Used ONLY for lexical/creative fields (vendor company names, product
names, short descriptions). It is NEVER trusted for IDs, dates,
statuses, or foreign keys — those are 100% deterministic by design
(see domain_rules.py / generators.py) so they can never be
hallucinated. Every LLM output is still re-validated by validators.py
before it is allowed into an INSERT.

If no API key is configured, the `groq` package isn't installed, or a
call fails for any reason, callers get `None` back and fall back to
local deterministic/Faker-based generation — the pipeline never
hard-depends on network access.
"""
import json
import logging
from typing import List, Optional

from config import LLM

logger = logging.getLogger("agent.llm")


class LLMClient:
    def __init__(self):
        self.enabled = LLM.enabled and bool(LLM.api_key)
        self._client = None
        if self.enabled:
            try:
                from groq import Groq
                self._client = Groq(api_key=LLM.api_key)
            except Exception as exc:  # pragma: no cover - environment dependent
                logger.warning(
                    "Groq client unavailable (%s). Falling back to deterministic "
                    "generation for all lexical fields.", exc,
                )
                self.enabled = False

    def generate_json_list(self, system_prompt: str, user_prompt: str, count: int) -> Optional[List[dict]]:
        """Ask the model for a JSON array of exactly `count` objects.
        Returns None on ANY failure (network, bad JSON, wrong shape) so
        the caller can fall back to local generation without crashing
        the pipeline."""
        if not self.enabled or self._client is None:
            return None
        for attempt in range(LLM.max_retries + 1):
            try:
                resp = self._client.chat.completions.create(
                    model=LLM.model,
                    temperature=LLM.temperature,
                    messages=[
                        {
                            "role": "system",
                            "content": system_prompt
                            + "\nRespond with ONLY a raw JSON array. No prose, no markdown fences, "
                              "no explanation, no keys other than the array itself.",
                        },
                        {"role": "user", "content": user_prompt},
                    ],
                )
                text = resp.choices[0].message.content.strip()
                text = text.strip("`")
                if text.lower().startswith("json"):
                    text = text[4:].strip()
                data = json.loads(text)
                if isinstance(data, dict):
                    for v in data.values():
                        if isinstance(v, list):
                            data = v
                            break
                if not isinstance(data, list) or not data:
                    raise ValueError("Model did not return a non-empty JSON array")
                return data[:count]
            except Exception as exc:
                logger.warning(
                    "LLM generation attempt %d/%d failed (%s).",
                    attempt + 1, LLM.max_retries + 1, exc,
                )
        return None