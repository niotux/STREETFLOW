import torch
from torch import nn


class RouteModel(nn.Module):
    def __init__(self, road_count, period_count, max_turns, hidden_size=64):
        super().__init__()
        self.hidden_size = hidden_size
        self.road_embedding = nn.Embedding(road_count, 16)
        self.period_embedding = nn.Embedding(period_count, 8)
        self.turn_embedding = nn.Embedding(max_turns + 2, 8)
        self.memory = nn.GRUCell(32, hidden_size)
        self.output = nn.Linear(hidden_size, max_turns + 1)

    def forward(self, road, previous_turn, period, hidden):
        features = torch.cat((self.road_embedding(road), self.turn_embedding(previous_turn), self.period_embedding(period)), dim=-1)
        hidden = self.memory(features, hidden)
        return self.output(hidden), hidden
