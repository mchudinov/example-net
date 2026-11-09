#!/usr/bin/env bash
# Renders a packaged chart and checks every label value against the Kubernetes rule.
#
# Usage: check-chart-labels.sh <chart.tgz> [helm template arguments...]
#
# The package must have build metadata in its version, for example 1.3.1+662a06b343e7, which is the
# form Flux gives a chart it installs from an OCIRepository. "+" is invalid in a label value, so a
# version like that exposes a template that puts the chart version in a label without escaping it.
#
# helm template and helm lint don't validate label values, and a default install reaches only some
# of the templates. Run this once per value set, so that together the sets render every template
# that sets labels.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <chart.tgz> [helm template arguments...]" >&2
  exit 2
fi

chart=$1
shift

# Without a "+" in the version an unescaped label is valid, and passing would prove nothing.
version=$(helm show chart "$chart" | tr -d '\r' | sed -n 's/^version: *//p')
if [[ $version != *+* ]]; then
  echo "::error::chart version '$version' has no build metadata, so the label check would prove nothing"
  exit 1
fi

echo "Checking labels at chart version $version with: ${*:-default values}"

# Line-based on purpose: rendered labels are plain block-style YAML, and awk is on every runner.
# The regex has no {n} intervals because the default awk on the runner is mawk.
helm template check "$chart" "$@" | tr -d '\r' | awk '
  # "# Source: <chart>/templates/<file>" comes before each rendered document.
  /^# Source: / {
    template = $3
    sub(/^[^\/]*\/templates\//, "", template)
    next
  }

  in_labels && NF == 0 { next }

  {
    match($0, /^ */)
    indent = RLENGTH
  }

  in_labels && indent <= labels_indent { in_labels = 0 }

  in_labels {
    entry = substr($0, indent + 1)
    colon = index(entry, ":")
    key = substr(entry, 1, colon - 1)
    value = substr(entry, colon + 1)
    gsub(/^ +| +$/, "", value)
    gsub(/^"|"$/, "", value)
    gsub(/^\047|\047$/, "", value)

    checked++
    if (key == "helm.sh/chart") chart_labels++

    if (length(value) > 63 || (value != "" && value !~ /^[A-Za-z0-9]([-A-Za-z0-9_.]*[A-Za-z0-9])?$/)) {
      printf "::error::%s: label %s has invalid value \"%s\"\n", template, key, value
      invalid++
    }
    next
  }

  /^ *labels: *$/ {
    in_labels = 1
    labels_indent = indent
  }

  END {
    if (chart_labels == 0) {
      print "::error::the render contains no helm.sh/chart label, so there is nothing to check"
      exit 1
    }
    if (invalid > 0) {
      printf "::error::%d of %d label values are invalid\n", invalid, checked
      exit 1
    }
    printf "All %d label values are valid\n", checked
  }
'
