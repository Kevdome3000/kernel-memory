#!/bin/bash

# Neo4j Docker setup for Kernel Memory testing
# Supports both interactive and CI modes
# Connection URI: neo4j://localhost:7687
# Username: neo4j
# Password: password (configurable via NEO4J_PASSWORD env var)
# Web UI: http://localhost:7474

# Configuration variables with defaults
NEO4J_PASSWORD=${NEO4J_PASSWORD:-"password"}
NEO4J_VERSION=${NEO4J_VERSION:-"5.15-enterprise"}
CONTAINER_NAME=${CONTAINER_NAME:-"neo4j-km-test"}
CI_MODE=${CI_MODE:-"false"}

# Set authentication
export NEO4J_AUTH="neo4j/${NEO4J_PASSWORD}"

echo "Starting Neo4j with vector support for Kernel Memory testing..."
echo "Version: ${NEO4J_VERSION}"
echo "Container: ${CONTAINER_NAME}"
echo "CI Mode: ${CI_MODE}"
echo "Web UI will be available at: http://localhost:7474"
echo "Bolt connection: neo4j://localhost:7687"
echo "Username: neo4j"
echo "Password: ${NEO4J_PASSWORD}"
echo ""

# Determine run mode (interactive vs CI)
if [ "$CI_MODE" = "true" ]; then
    # CI mode: detached, with health checks
    echo "Running in CI mode (detached)..."
    docker run -d --rm --name ${CONTAINER_NAME} \
      -p 7474:7474 \
      -p 7687:7687 \
      -e NEO4J_AUTH \
      -e NEO4J_PLUGINS='["apoc"]' \
      -e NEO4J_apoc_export_file_enabled=true \
      -e NEO4J_apoc_import_file_enabled=true \
      -e NEO4J_apoc_import_file_use__neo4j__config=true \
      -e NEO4J_ACCEPT_LICENSE_AGREEMENT=yes \
      -e NEO4J_dbms_memory_heap_initial__size=512m \
      -e NEO4J_dbms_memory_heap_max__size=2G \
      -e NEO4J_dbms_memory_pagecache_size=1G \
      -e NEO4J_dbms_connector_bolt_listen__address=0.0.0.0:7687 \
      -e NEO4J_dbms_connector_http_listen__address=0.0.0.0:7474 \
      --health-cmd="cypher-shell -u neo4j -p ${NEO4J_PASSWORD} 'RETURN 1'" \
      --health-interval=10s \
      --health-timeout=10s \
      --health-retries=5 \
      ${NEO4J_VERSION}
    
    # Wait for Neo4j to be ready
    echo "Waiting for Neo4j to be ready..."
    timeout=60
    counter=0
    while [ $counter -lt $timeout ]; do
        if docker exec ${CONTAINER_NAME} cypher-shell -u neo4j -p ${NEO4J_PASSWORD} "RETURN 1" > /dev/null 2>&1; then
            echo "Neo4j is ready!"
            
            # Enable vector indexes and verify setup
            echo "Configuring vector support..."
            docker exec ${CONTAINER_NAME} cypher-shell -u neo4j -p ${NEO4J_PASSWORD} "CALL dbms.procedures() YIELD name WHERE name CONTAINS 'vector' RETURN count(name) as vectorProcedures"
            
            echo "Neo4j started successfully in CI mode."
            echo "Use 'docker stop ${CONTAINER_NAME}' to stop the container."
            exit 0
        fi
        sleep 2
        counter=$((counter + 2))
        echo "Waiting... ($counter/${timeout}s)"
    done
    
    echo "ERROR: Neo4j failed to start within ${timeout} seconds"
    docker logs ${CONTAINER_NAME}
    docker stop ${CONTAINER_NAME} 2>/dev/null
    exit 1
else
    # Interactive mode: for local development
    echo "Running in interactive mode..."
    docker run -it --rm --name ${CONTAINER_NAME} \
      -p 7474:7474 \
      -p 7687:7687 \
      -e NEO4J_AUTH \
      -e NEO4J_PLUGINS='["apoc"]' \
      -e NEO4J_apoc_export_file_enabled=true \
      -e NEO4J_apoc_import_file_enabled=true \
      -e NEO4J_apoc_import_file_use__neo4j__config=true \
      -e NEO4J_ACCEPT_LICENSE_AGREEMENT=yes \
      -e NEO4J_dbms_memory_heap_initial__size=512m \
      -e NEO4J_dbms_memory_heap_max__size=2G \
      -e NEO4J_dbms_memory_pagecache_size=1G \
      -e NEO4J_dbms_connector_bolt_listen__address=0.0.0.0:7687 \
      -e NEO4J_dbms_connector_http_listen__address=0.0.0.0:7474 \
      ${NEO4J_VERSION}
    
    echo ""
    echo "Neo4j stopped. Container removed."
fi