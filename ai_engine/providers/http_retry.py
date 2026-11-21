"""Parse Retry-After seconds or HTTP dates with bounded waiting times."""

from datetime import UTC, datetime
from email.utils import parsedate_to_datetime


def retry_after_seconds(
    value: str | None,
    default: float = 10.0,
    cap: float = 60.0,
    now: datetime | None = None,
) -> float:
    wait = default
    if value is not None:
        try:
            wait = float(value)
        except ValueError:
            try:
                retry_at = parsedate_to_datetime(value)
                if retry_at.tzinfo is None:
                    retry_at = retry_at.replace(tzinfo=UTC)
                current = now if now is not None else datetime.now(UTC)
                if current.tzinfo is None:
                    current = current.replace(tzinfo=UTC)
                wait = (retry_at - current).total_seconds()
            except (TypeError, ValueError, OverflowError):
                wait = default
    return max(0.0, min(wait, cap))
