# Briefyn AI

[![Tests](https://github.com/sametulas57/BriefynAI/actions/workflows/test.yml/badge.svg)](https://github.com/sametulas57/BriefynAI/actions/workflows/test.yml)

Briefyn is an AI meeting assistant for teams. Upload a recording or connect Zoom, and Briefyn produces a transcript, summary, key decisions, action items, and open topics in a searchable team archive.

**Status:** pre-launch. The application runs locally with Docker; it is not yet in production.

## Features

- **Meeting analysis:** transcription and structured summaries from uploaded recordings or Zoom cloud recordings.
- **Team workspaces:** invitations, roles, comments, and meeting summaries shared by email.
- **Action tracking:** assignable action items, a personal task page, and email reminders for overdue tasks.
- **Meeting series:** recurring meetings with a live agenda built from open tasks and pending topics.
- **Ask the Archive:** natural-language questions across past meetings, answered with the source meeting (RAG with keyword retrieval).
- **Zoom integration:** OAuth 2.0 connection and webhooks that import cloud recordings, including speaker names.
- **Privacy:** encrypted transcripts, audio deleted after processing, and an AI provider running with zero data retention.

## Architecture

```mermaid
flowchart LR
    U[Browser] --> W[ASP.NET Core web app]
    Z[Zoom] -->|webhooks| W
    W --> D[(SQL Server)]
    W -->|Hangfire jobs| G[Groq: Whisper and LLMs]
    W -->|Optional transcription| A[Azure Speech]
    W -->|Audio preparation| F[ffmpeg]
    W -->|SignalR| U
```

- `MeetingWeb/`: ASP.NET Core Razor Pages app with Identity, EF Core, Hangfire, and SignalR.
- `MeetingWeb.Tests/`: xUnit suite.

## Getting started

Requires Docker and a [Groq](https://groq.com) API key.

```bash
cp .env.example .env
```

Fill in `.env`.

For local development and tests on macOS, install ffmpeg: `brew install ffmpeg`.

Public pages use Tailwind CSS v4 and daisyUI 5, built with npm (Node 24+). Install once, and again whenever `package-lock.json` changes:

```bash
npm ci
npm run css:build
```

While editing public pages, keep the watcher running so the container picks up changes:

```bash
npm run css:watch
```

Then start the stack:

```bash
docker compose up -d --build
```

The app runs at http://127.0.0.1:8080.

## Tests

```bash
dotnet test MeetingWeb.Tests
```

`scripts/verify.sh` runs everything at once (CSS build, format, .NET build and tests); the pre-commit hook and CI run the same script.

Tests run on every push with GitHub Actions.
