import React, { useMemo } from "react";

function GraphCanvas({ graph }) {
  const nodes = graph?.nodes || [];
  const edges = graph?.edges || [];
  const positions = useMemo(() => layoutNodes(nodes), [nodes]);
  const byId = new Map(positions.map((n) => [n.id, n]));
  return (
    <svg className="graph-canvas" viewBox="0 0 900 560" role="img" aria-label="Knowledge graph">
      <defs>
        <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
          <path d="M0,0 L0,6 L9,3 z" fill="#94a3b8" />
        </marker>
      </defs>
      {edges.map((edge) => {
        const a = byId.get(edge.source);
        const b = byId.get(edge.target);
        if (!a || !b) return null;
        return (
          <g key={edge.id}>
            <line x1={a.x} y1={a.y} x2={b.x} y2={b.y} className="graph-edge" markerEnd="url(#arrow)" />
            <text x={(a.x + b.x) / 2} y={(a.y + b.y) / 2 - 8} textAnchor="middle">{edge.type.replaceAll("_", " ")}</text>
          </g>
        );
      })}
      {positions.map((node) => (
        <g key={node.id}>
          <circle cx={node.x} cy={node.y} r={node.type === "Person" ? 34 : 25} className={`graph-node ${node.type.toLowerCase()}`} />
          <text x={node.x} y={node.y + 4} textAnchor="middle" className="node-code">{node.type.slice(0, 2).toUpperCase()}</text>
          <text x={node.x} y={node.y + 50} textAnchor="middle" className="node-label">{node.label}</text>
        </g>
      ))}
    </svg>
  );
}

function layoutNodes(nodes) {
  const center = { x: 450, y: 280 };
  const radius = 190;
  return nodes.map((node, index) => {
    if (index === 0) return { ...node, ...center };
    const angle = (Math.PI * 2 * (index - 1)) / Math.max(1, nodes.length - 1) - Math.PI / 2;
    return { ...node, x: center.x + Math.cos(angle) * radius, y: center.y + Math.sin(angle) * radius };
  });
}

export { GraphCanvas };