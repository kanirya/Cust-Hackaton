---
name: Intelligence Deep Blue
colors:
  surface: '#131315'
  surface-dim: '#131315'
  surface-bright: '#39393b'
  surface-container-lowest: '#0e0e10'
  surface-container-low: '#1b1b1d'
  surface-container: '#1f1f21'
  surface-container-high: '#2a2a2b'
  surface-container-highest: '#353436'
  on-surface: '#e4e2e4'
  on-surface-variant: '#c6c6cd'
  inverse-surface: '#e4e2e4'
  inverse-on-surface: '#303032'
  outline: '#909097'
  outline-variant: '#45464d'
  surface-tint: '#bec6e0'
  primary: '#bec6e0'
  on-primary: '#283044'
  primary-container: '#0f172a'
  on-primary-container: '#798098'
  inverse-primary: '#565e74'
  secondary: '#4edea3'
  on-secondary: '#003824'
  secondary-container: '#00a572'
  on-secondary-container: '#00311f'
  tertiary: '#dec29a'
  on-tertiary: '#3e2d11'
  tertiary-container: '#231500'
  on-tertiary-container: '#957d5a'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#dae2fd'
  primary-fixed-dim: '#bec6e0'
  on-primary-fixed: '#131b2e'
  on-primary-fixed-variant: '#3f465c'
  secondary-fixed: '#6ffbbe'
  secondary-fixed-dim: '#4edea3'
  on-secondary-fixed: '#002113'
  on-secondary-fixed-variant: '#005236'
  tertiary-fixed: '#fcdeb5'
  tertiary-fixed-dim: '#dec29a'
  on-tertiary-fixed: '#271901'
  on-tertiary-fixed-variant: '#574425'
  background: '#131315'
  on-background: '#e4e2e4'
  surface-variant: '#353436'
  risk-low: '#10B981'
  risk-medium: '#F59E0B'
  risk-high: '#EF4444'
  risk-critical: '#991B1B'
  surface-border: '#1E293B'
  surface-subtle: '#334155'
typography:
  display-lg:
    fontFamily: Inter
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 56px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Inter
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
    letterSpacing: -0.01em
  headline-lg-mobile:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  headline-md:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  body-lg:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 28px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-md:
    fontFamily: JetBrains Mono
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.05em
  caption:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '500'
    lineHeight: 14px
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  base: 4px
  gutter: 16px
  margin-desktop: 24px
  column-gap: 20px
  sidebar-width: 320px
  drawer-width: 480px
---

## Brand & Style

The design system is engineered for "Government-grade security meets modern intelligence." It targets tax auditors, intelligence analysts, and policy makers who require absolute clarity in high-stakes environments. The brand personality is **authoritative, transparent, and clinical**, aimed at transforming complex graph data into actionable legal evidence.

The design style is **Corporate / Modern** with a focus on **Information Density**. It utilizes a systematic, layered approach where whitespace is treated as a functional tool for cognitive de-cluttering rather than mere decoration. The interface feels like a high-end SaaS platform for analysts—minimalist in its chrome but rich in its data hierarchy, ensuring that AI-generated insights are always grounded in visible, structured evidence.

## Colors

The palette is anchored by **Intelligence Deep Blue**, establishing a "Mission Control" environment that reduces eye strain during long investigative sessions. The system defaults to **dark mode** to emphasize luminous data points and graph connections.

A semantic risk scale is strictly enforced:
- **Compliance Emerald (#10B981)**: Indicates low-risk or verified "healthy" status.
- **Deviation Amber (#F59E0B)**: Used for medium-risk anomalies requiring secondary review.
- **Risk Crimson (#EF4444)**: Reserved for high-risk alerts and critical policy violations.

Neutral tones are derived from the primary Slate/Zinc scale to maintain a monochromatic technical feel, ensuring that chromatic colors are used exclusively for functional signaling and risk categorization.

## Typography

The system utilizes **Inter** for its exceptional legibility in data-dense layouts. For technical identifiers, correlation IDs, and node labels, **JetBrains Mono** is employed to distinguish raw data from UI labels.

- **Display levels** are used for national-level KPIs (e.g., total recoverable tax).
- **Body levels** prioritize "plain language" explanations to ensure the AI's "Explainability" remains accessible.
- **Label levels** use monospaced fonts for CNICs and Case IDs to ensure character-level clarity and alignment in tables.

## Layout & Spacing

The layout follows a **structured 12-column fluid grid** for high-level dashboards, transitioning to a **Case Workspace model** for investigations. 

- **Case Workspace**: Features a fixed left profile summary, a fluid central "Graph Explorer," and a right-side "Evidence Drawer."
- **Rhythm**: A strict 4px/8px baseline grid ensures alignment across dense data tables and nested evidence cards.
- **Reflow**: On mobile/tablet, the right-side drawer transitions to a full-screen overlay. The Graph Explorer remains interactive but adopts simplified gesture controls.

## Elevation & Depth

This design system uses **Tonal Layers** and **Low-contrast Outlines** rather than traditional shadows to maintain a clean, technical aesthetic. 

- **Level 0 (Background)**: The deepest primary blue (#0F172A).
- **Level 1 (Cards)**: Elevated via a subtle border (#1E293B) and a slightly lighter surface (#141E33).
- **Level 2 (Active/Hover)**: Utilizes a 1px solid stroke of the primary color or semantic risk color.
- **Drawers & Modals**: Use a high-degree backdrop blur (12px) to maintain context of the graph behind the evidence being reviewed. This "glassmorphism" is purely functional, ensuring the user never loses sight of the data relationships.

## Shapes

The shape language is **precise and geometric**. Rounded corners are kept to a minimum (4px) to preserve a serious, institutional feel. 

- **Entity Nodes**: Use distinct geometric primitives (Circles for Persons, Hexagons for Businesses, Squares for Assets) to allow for quick visual scanning of the knowledge graph.
- **Status Pills**: Use fully rounded (pill-shaped) geometry to differentiate "Status" indicators from "Action" buttons.

## Components

### Risk Scoring
- **0-100 Scales**: Visualized as a semi-circular radial gauge or a linear "segmented" bar. Color mapping must strictly follow the semantic risk tokens.
- **Score Breakdown**: Evidence is nested within accordion-style "Score Cards" explaining the contribution of each anomaly to the total score.

### Knowledge Graph Visualization
- **Nodes**: High-contrast icons with a mono-spaced ID label.
- **Edges**: Directional lines with weighted thickness representing "Financial Flow" or "Relationship Strength."

### Evidence Cards & Audit Trails
- **Evidence Cards**: Border-left colored by risk level. Includes a "Policy Citation" footer and a "Confidence Score" badge.
- **Audit Trails**: Vertical chronological steppers with micro-monospaced timestamps and user action logs.

### Professional Data Tables
- **Grid**: Zebra-striping is disabled; use 1px horizontal dividers only.
- **Filters**: Compact "Filter Chips" at the top of the table for rapid data drilling.

### AI Assistant
- **Chat Drawer**: Positioned on the right. Messages are structured—never just text. AI responses must include "Citations" that link directly to highlighted nodes in the Graph or rows in the Data Table.
- **Input**: Command-K style interface for natural language querying of tax records.