<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class FinancialReport extends Model
{
    use HasFactory;

    protected $table = 'financial_reports';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'title',
        'description',
        'revenue',
        'status',
    ];

    protected static function booted(): void
    {
        static::creating(function (FinancialReport $report) {
            if (empty($report->id)) {
                $report->id = 'req_' . Str::lower(Str::random(12));
            }
            if (empty($report->status)) {
                $report->status = 'active';
            }
        });
    }
}
