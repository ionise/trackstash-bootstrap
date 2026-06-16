# trackstash-bootstrap

## Overview
`trackstash-bootstrap` is part of the TrackStash project.

Its role is orchestration and bootstrap, not domain or storage ownership.

## Purpose
- Initialize the database.
- Run migrations.
- Seed or import starter data.
- Trigger initial workflows.

## Architecture
This module may be implemented as a console application or worker service.

It should depend on shared contracts and concrete adapters, specifically:
- `trackstash-core`
- storage adapter modules (for example SQLite)

## Boundaries
- Keep bootstrap concerns here.
- Do not move core domain contracts into this module.
- Do not make this module the owner of storage internals.
