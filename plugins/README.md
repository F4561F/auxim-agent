# Plugins

Place runtime plugin assemblies here.

Auxim scans `./plugins` and `~/.auxim/plugins` for DLLs. A plugin implements
`IAuximPlugin` and registers additional tools with `ToolRegistry`.
