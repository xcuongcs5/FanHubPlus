<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class Faq extends Model
{
    use HasFactory;

    protected $table = 'cms_faqs';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'question',
        'answer',
        'category',
        'is_active',
    ];

    protected $casts = [
        'is_active' => 'boolean',
    ];

    protected static function booted(): void
    {
        static::creating(function (Faq $item) {
            if (empty($item->id)) {
                $item->id = 'faq_' . Str::lower(Str::random(12));
            }
            if (!isset($item->is_active)) {
                $item->is_active = true;
            }
        });
    }
}
