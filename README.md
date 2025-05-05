# DevOps Portfolio Project

![CI/CD Pipeline](https://img.shields.io/github/actions/workflow/status/ctaniclab/devops-portfolio/docker-build.yml)

Complete DevOps pipeline demonstrating:

- Infrastructure as Code (Terraform)
- CI/CD (GitHub Actions)
- Containerization (Docker)
- Orchestration (Kubernetes)
- Monitoring (Prometheus/Grafana)
- Logging (ELK Stack)

## Quick Start

```bash
# Clone repository
git clone https://github.com/ctaniclab/devops-portfolio.git
cd devops-portfolio

# Initialize infrastructure
cd infrastructure/terraform
terraform init
terraform plan
terraform apply

# Deploy application
kubectl apply -f kubernetes/
Project Structure
devops-portfolio/
├── .github/workflows/    # CI/CD pipelines
├── infrastructure/       # IaC configurations
├── kubernetes/          # K8s manifests
├── scripts/             # Utility scripts
└── README.md            # Project documentation
Badges

https://img.shields.io/badge/terraform-%25235835CC.svg?style=for-the-badge&logo=terraform&logoColor=white
https://img.shields.io/badge/kubernetes-%2523326ce5.svg?style=for-the-badge&logo=kubernetes&logoColor=white
https://img.shields.io/badge/docker-%25230db7ed.svg?style=for-the-badge&logo=docker&logoColor=white