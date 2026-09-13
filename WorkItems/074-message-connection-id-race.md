# 074 — Fix the `_lastMessageConnectionID` race in `MessageBusHost_Networked`

**Status**: done
**Related**: #088 (per-player request routing), audit §6; engine `975d857` / merge `75f9f1d`, bump (superproject)

## Goal
Each connected client has its own read loop, so with 2+ remote clients concurrent dispatches overwrote the host's ambient `_lastMessageConnectionID` field. `GetCurrentMessageConnectionID` (used to answer `RequestAllDataMessage` and the lobby greeting) could then return the *other* client's connection — misrouted full-state sync. Done when the source `ConnectionID` is threaded through the dispatch/registrar callback signature instead of ambient state, and nothing reads an ambient "current connection".

## Notes
- 2026-06-21: Implemented the audit's prescription (chosen over the smaller `AsyncLocal` ambient alternative, which would have preserved the hacky pattern). `IMessageRegistrar`/`MessageRegistrar` gained `RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID>)` + dereg; `DispatchToHandlers` now takes `ConnectionID? connectionID = null`. At dispatch a 2-param handler receives the connection; a locally-originated dispatch (no connection) skips connection-aware handlers but still runs plain ones. `GetCurrentMessageConnectionID` and `_lastMessageConnectionID` deleted from `IMessageBusHost` + `MessageBusHost_Networked`. Two consumers converted to connection-aware handlers: `GameDataUpdateSender.OnReceivedRequestAllDataMessage` and `LobbyViewModel_Host.OnReceiveNewClientGreeting`/`OnResumeClientGreeting`. App-side `LocalMessageBus` and the `MockMessageBusHost` test stub updated to the new interface. New `ConnectionMessageDispatchTests` (incl. a 5000-iteration concurrent no-cross-talk regression). Suite 611/0; full build clean; headless smoke exit 0.

## Decisions
- **Handler-shape detection via parameter count** (`del.Method.GetParameters().Length == 2`) rather than separate handler dictionaries — keeps deregistration-by-reference working against a single `List<Delegate>` and avoids a parallel registry. Reliable because a lambda's `Method` reports its declared parameters (captures live on `del.Target`).
- **Skip connection-aware handlers on local dispatch** (null connection) rather than synthesizing a host id at the registrar level. The only connection-aware handlers (full-sync, greeting) never fire on locally-originated messages, so skipping is correct and keeps "no connection" explicit. `LocalMessageBus` does pass `ConnectionID.Host` for its single-machine dispatch since there it *is* the connection.

## Outcome
Connection id is now a dispatch parameter, eliminating the cross-talk window entirely. Nothing reads ambient connection state. Follow-up #088 (route requests to the target connection instead of broadcasting) is the natural next step and now has a clean per-handler connection to build on.
