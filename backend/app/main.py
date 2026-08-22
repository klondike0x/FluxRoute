from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.routes import status


app = FastAPI(title="FluxRoute API", version="2.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(status.router, prefix="/api")
