# DevOps Portfolio Project

![CI/CD Pipeline](https://img.shields.io/github/actions/workflow/status/ctaniclab/devops-portfolio/docker-build.yml)

Complete DevOps pipeline demonstrating:

- Infrastructure as Code (Terraform)
- CI/CD (GitHub Actions)
- Containerization (Docker)
- Orchestration (Kubernetes)
- Monitoring (Prometheus/Grafana)
- Logging (ELK Stack)

Project Structure
devops-portfolio/
├── .github/workflows/    # CI/CD pipelines
├── infrastructure/       # IaC configurations
├── kubernetes/          # K8s manifests
├── scripts/             # Utility scripts
└── README.md            # Project documentation
Badges

![image](https://github.com/user-attachments/assets/5e1e5bd2-55e1-431f-9cb7-bc6e994d95c8)

![image](https://github.com/user-attachments/assets/3536c56c-aa6b-4e6f-a75f-006eb21c92b4)

![image](https://github.com/user-attachments/assets/b4066791-e55c-4fbe-b274-bd28394427e6)


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
