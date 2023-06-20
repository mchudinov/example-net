#!/bin/bash
docker login
docker build -t example-net-console .
docker tag example-net-console chudinov/example-net-console
docker push chudinov/example-net-console
